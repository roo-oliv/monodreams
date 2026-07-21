#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the CE-B <b>collider migrator</b> (<see cref="ColliderMigration"/>, the
/// <c>monodreams migrate-colliders</c> core): the version-1 → version-2 transform (box <c>bounds</c> →
/// centered <c>size</c> on a collider entity; convex → collider child, verbatim), idempotence, the
/// per-file summary, directory recursion, dry-run, and — the CE pre-mortem #3 tripwire — that
/// <c>migrate → load → save</c> is a byte fixed point over fixtures replicating the committed shapes and a
/// realistic island-like scene.
///
/// Pure logic — hand-built canonical version-1 fixtures + the real reader/writer round-trip; no
/// <c>GraphicsDevice</c>, no user files.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class ColliderMigrationTests
{
    // ---- Fixture helpers: build canonical version-1 component bodies (the OLD shapes) ----

    private static JsonElement El(object value) => CanonicalJson.SerializeToElement(value);
    private static JsonElement Transform(float x, float y) =>
        El(new { position = new[] { x, y }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
    private static JsonElement Info(string type, string name) => El(new { type, name });
    private static JsonElement Body() => El(new
    {
        mass = 1f, gravityActive = true, gravityFactor = 1f, isKinematic = false,
        freezeRotation = false, freezePositionX = false, freezePositionY = false,
    });

    /// <summary>An OLD (version-1) box body: <c>{ bounds:[x,y,w,h], activeLayers, passive, enabled }</c>.</summary>
    private static JsonElement BoxBounds(int x, int y, int w, int h, int[]? layers = null, bool passive = false, bool enabled = true) =>
        El(new { bounds = new[] { x, y, w, h }, activeLayers = layers ?? new[] { -1 }, passive, enabled });

    /// <summary>A convex body (identical between v1 and v2 — only its home entity changes).</summary>
    private static JsonElement Convex(float[][]? verts = null, int[]? layers = null, bool passive = false, bool enabled = true, bool ignoreRot = true) =>
        El(new
        {
            modelVertices = verts ?? new[] { new[] { -8f, -8f }, new[] { 8f, -8f }, new[] { 8f, 8f }, new[] { -8f, 8f } },
            activeLayers = layers ?? new[] { -1 },
            passive,
            enabled,
            ignoreTransformRotation = ignoreRot,
        });

    private static SceneEntityData Entity(int? id, int? parent, params (string Key, JsonElement Value)[] comps)
    {
        var e = new SceneEntityData { Id = id, Parent = parent };
        foreach (var (k, v) in comps) e.Components[k] = v;
        return e;
    }

    private const string Box = ColliderMigration.BoxColliderKey;
    private const string Convx = ColliderMigration.ConvexColliderKey;
    private const string Xf = ColliderMigration.TransformKey;
    private const string Ei = ColliderMigration.EntityInfoKey;

    private static string V1Json(SceneData scene) { scene.Version = 1; return CanonicalJson.Serialize(scene); }

    // ---- The migrator's key constants must not drift from the engine serializer keys ----

    [Fact]
    public void MigratorKeys_MatchEngineSerializerKeys()
    {
        Assert.Equal(EngineComponentSerializers.BoxColliderKey, ColliderMigration.BoxColliderKey);
        Assert.Equal(EngineComponentSerializers.ConvexColliderKey, ColliderMigration.ConvexColliderKey);
        Assert.Equal(EngineComponentSerializers.TransformKey, ColliderMigration.TransformKey);
        Assert.Equal(EngineComponentSerializers.EntityInfoKey, ColliderMigration.EntityInfoKey);
        Assert.Equal(EngineComponentSerializers.SpriteInfoKey, ColliderMigration.SpriteInfoKey);
        Assert.Equal(EngineComponentSerializers.RigidBodyKey, ColliderMigration.RigidBodyKey);
        Assert.Equal(EngineComponentSerializers.VelocityKey, ColliderMigration.VelocityKey);
        // The COLLIDER lift targets version 2 — decoupled from SceneVersionGuard.CurrentVersion (now 3,
        // which the separate CM camera lift reaches). A v1 collider file migrates to v2; the version guard
        // then re-saves a camera-less v2 as v3, or refuses a v2 with a camera block until `monodreams migrate`.
        Assert.Equal(2, ColliderMigration.TargetVersion);
    }

    // ---- Box on a dedicated collider carrier → reshaped IN PLACE (bounds → centered size) ----

    [Fact]
    public void Box_OnDedicatedCarrier_ReshapesInPlace_CenteredSize_TransformNudgedByBoundsCentre()
    {
        // A bare box-only root: bounds [-29,-17,58,35] — centre (0, 0.5), so the transform is nudged to (0, 0.5).
        var scene = new SceneData();
        scene.Entities.Add(Entity(0, null, (Box, BoxBounds(-29, -17, 58, 35, passive: true)), (Xf, Transform(0, 0))));

        var result = ColliderMigration.Migrate(V1Json(scene), "fixture.mdscene");

        Assert.True(result.Changed);
        Assert.Equal(1, result.BoxesReshapedInPlace);
        Assert.Equal(0, result.CollidersMovedToChild);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(2, migrated.Version);
        Assert.Single(migrated.Entities);                    // no child added — reshaped in place
        var e = migrated.Entities[0];

        var box = e.Components[Box];
        Assert.False(box.TryGetProperty("bounds", out _));   // "bounds" is gone
        Assert.Equal(new[] { 58f, 35f }, box.GetProperty("size").EnumerateArray().Select(v => v.GetSingle()).ToArray());
        Assert.True(box.GetProperty("passive").GetBoolean()); // preserved

        var pos = e.Components[Xf].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Assert.Equal(new[] { 0f, 0.5f }, pos);               // nudged by the bounds centre
    }

    // ---- Box on a NON-dedicated owner (has a body) → moved to a NEW child at the bounds centre ----

    [Fact]
    public void Box_OnBodyEntity_MovesToChild_AtBoundsCentre_LayersPreserved()
    {
        var scene = new SceneData();
        // A wall-like body: box bounds [-8,-12,16,24] (centre (0,0)), custom layers.
        scene.Entities.Add(Entity(0, null,
            (Ei, Info("Wall", "wall")), (Body_Key, Body()),
            (Box, BoxBounds(-8, -12, 16, 24, layers: new[] { 2, 5 }, passive: true)), (Xf, Transform(100, 50))));

        var result = ColliderMigration.Migrate(V1Json(scene), "fixture.mdscene");
        Assert.Equal(1, result.CollidersMovedToChild);
        Assert.Equal(1, result.ChildEntitiesAdded);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(2, migrated.Entities.Count);            // owner + new collider child
        var owner = migrated.Entities[0];
        var child = migrated.Entities[1];

        Assert.False(owner.Components.ContainsKey(Box));     // stripped from the owner
        Assert.True(owner.Components.ContainsKey(Body_Key));  // body stays on the owner
        Assert.Equal(0, child.Parent);                        // child parents to the owner (index 0)
        Assert.Equal(new[] { 0f, 0f }, child.Components[Xf].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray());
        Assert.Equal(new[] { 16f, 24f }, child.Components[Box].GetProperty("size").EnumerateArray().Select(v => v.GetSingle()).ToArray());
        Assert.Equal(new[] { 2, 5 }, child.Components[Box].GetProperty("activeLayers").EnumerateArray().Select(v => v.GetInt32()).ToArray());
    }

    // ---- Convex on a NON-dedicated owner (sprite/body) → moved to a child at origin, verts verbatim ----

    [Fact]
    public void Convex_OnBodyEntity_MovesToChild_AtOrigin_VerticesVerbatim()
    {
        var verts = new[] { new[] { -1.03f, 4.78f }, new[] { 0.23f, 4.57f }, new[] { 5.78f, 5.05f }, new[] { -5.01f, 6.45f } };
        var scene = new SceneData();
        scene.Entities.Add(Entity(0, null,
            (Ei, Info("NPC", "boldo")), (Body_Key, Body()),
            (Convx, Convex(verts, ignoreRot: true)), (Xf, Transform(79.6936f, -33.1469f))));

        var result = ColliderMigration.Migrate(V1Json(scene), "fixture.mdscene");
        Assert.Equal(1, result.CollidersMovedToChild);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        var child = migrated.Entities[1];
        Assert.Equal(0, child.Parent);
        Assert.Equal(new[] { 0f, 0f }, child.Components[Xf].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray());

        // Verts are copied verbatim (no re-basing) and ignoreTransformRotation is preserved.
        var mv = child.Components[Convx].GetProperty("modelVertices");
        Assert.Equal(4, mv.GetArrayLength());
        Assert.Equal(-1.03f, mv[0][0].GetSingle());
        Assert.Equal(4.78f, mv[0][1].GetSingle());
        Assert.True(child.Components[Convx].GetProperty("ignoreTransformRotation").GetBoolean());
    }

    // ---- A dialogue/trigger ZONE (a zone component + a box, no sprite/body) reshapes IN PLACE so the
    //      zone identity stays ON the collider entity (design pre-mortem #4: consumers read it off ColliderB) ----

    [Fact]
    public void Box_OnZoneEntity_ReshapesInPlace_KeepingTheZoneComponentOnTheCollider()
    {
        // Mirrors the committed dialogue-zone shape: { game.DialogueZone, core.BoxCollider, EntityInfo, Transform }.
        var scene = new SceneData();
        scene.Entities.Add(Entity(0, null,
            ("game.DialogueZone", El(new { node = "Boldo_Start" })),
            (Ei, Info("Zone", "BoldoZone")),
            (Box, BoxBounds(-23, -26, 46, 52, passive: true)),
            (Xf, Transform(0, 0))));

        var result = ColliderMigration.Migrate(V1Json(scene), "zone.mdscene");
        Assert.Equal(1, result.BoxesReshapedInPlace);
        Assert.Equal(0, result.CollidersMovedToChild);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Single(migrated.Entities);                          // no child — the zone IS the collider entity
        var zone = migrated.Entities[0];
        Assert.True(zone.Components.ContainsKey(Box));              // the (reshaped) box stays ON the zone
        Assert.True(zone.Components.ContainsKey("game.DialogueZone")); // …together with its zone identity
        Assert.Equal(new[] { 46f, 52f }, zone.Components[Box].GetProperty("size").EnumerateArray().Select(v => v.GetSingle()).ToArray());
    }

    // ---- Convex on a dedicated carrier (a trigger-like standalone collider) → left unchanged ----

    [Fact]
    public void Convex_OnDedicatedCarrier_IsUnchanged()
    {
        var scene = new SceneData();
        scene.Entities.Add(Entity(0, null, (Ei, Info("Zone", "trigger")), (Convx, Convex()), (Xf, Transform(10, 20))));

        var result = ColliderMigration.Migrate(V1Json(scene), "fixture.mdscene");
        Assert.Equal(0, result.CollidersMovedToChild);
        Assert.Equal(0, result.BoxesReshapedInPlace);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Single(migrated.Entities);                    // no child — a named bare collider IS the entity
        Assert.True(migrated.Entities[0].Components.ContainsKey(Convx));
        Assert.Equal(2, migrated.Version);
    }

    // ---- Idempotence: a version-2 input is a reported no-op with byte-identical output ----

    [Fact]
    public void AlreadyVersion2_IsANoOp_BytesUnchanged()
    {
        var scene = new SceneData(); // Version defaults to 2
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "p")), (Xf, Transform(1, 2))));
        var v2 = CanonicalJson.Serialize(scene);

        var result = ColliderMigration.Migrate(v2, "already.mdscene");
        Assert.True(result.AlreadyCurrent);
        Assert.False(result.Changed);
        Assert.Equal(v2, result.Json);

        // And migrating the migrator's OWN output again is a no-op (double-migrate = single-migrate).
        var scene1 = new SceneData();
        scene1.Entities.Add(Entity(0, null, (Box, BoxBounds(-8, -8, 16, 16)), (Xf, Transform(0, 0))));
        var once = ColliderMigration.Migrate(V1Json(scene1), "f.mdscene").Json;
        var twice = ColliderMigration.Migrate(once, "f.mdscene");
        Assert.True(twice.AlreadyCurrent);
        Assert.Equal(once, twice.Json);
    }

    // ---- Unparseable input fails loud ----

    [Fact]
    public void UnparseableInput_ThrowsLoud()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ColliderMigration.Migrate("{ not valid json", "bad.mdscene"));
        Assert.Contains("bad.mdscene", ex.Message);
    }

    // ---- Byte fixed point: migrate → load (real reader) → save (real writer) == migrate ----

    [Fact]
    public void MigrateLoadSave_IsAByteFixedPoint_OnCommittedLikeShapes()
    {
        // Mirrors the committed Blender_Level shapes: convex on visual/body roots (→ child), box on a
        // dedicated child (→ reshaped in place), a dedicated convex trigger (→ unchanged), a bare box root
        // with a non-centered bounds (→ reshaped in place, transform nudged).
        var scene = new SceneData();
        scene.Entities.Add(Entity(0, null, (Ei, Info("NPC", "boldo")), (Body_Key, Body()), (Convx, Convex()), (Xf, Transform(80, -33))));
        scene.Entities.Add(Entity(null, 0, (Box, BoxBounds(-23, -26, 46, 52, passive: true)), (Xf, Transform(0, 0)))); // box child of boldo
        scene.Entities.Add(Entity(1, null, (Ei, Info("Wall", "wall")), (Body_Key, Body()), (Box, BoxBounds(-8, -12, 16, 24, passive: true)), (Xf, Transform(200, 0))));
        scene.Entities.Add(Entity(2, null, (Ei, Info("Zone", "trigger")), (Convx, Convex(passive: true)), (Xf, Transform(-40, 12))));
        scene.Entities.Add(Entity(3, null, (Box, BoxBounds(-29, -17, 58, 35, passive: true)), (Xf, Transform(0, 0))));

        AssertMigrateLoadSaveFixedPoint(V1Json(scene));
    }

    [Fact]
    public void MigrateLoadSave_IsAByteFixedPoint_OnRealisticIslandLikeScene()
    {
        // A larger island-like scene: many props, a player body with a box child, several static footprints
        // (boxes on bare collision nodes), a couple of convex hulls on visual props, nested children.
        var scene = new SceneData();
        var rng = new Random(1234);
        var nextId = 0;
        // Player body + box collider child.
        scene.Entities.Add(Entity(nextId++, null, (Ei, Info("Player", "pete")), (Body_Key, Body()), (Convx, Convex()), (Xf, Transform(48, 20))));
        var playerIndex = scene.Entities.Count - 1;
        scene.Entities.Add(Entity(null, playerIndex, (Box, BoxBounds(-8, -18, 16, 36)), (Xf, Transform(0, 0))));
        // A row of static footprint boxes (bare collision nodes → reshaped in place).
        for (var i = 0; i < 6; i++)
        {
            var w = 20 + rng.Next(0, 40);
            var h = 12 + rng.Next(0, 30);
            scene.Entities.Add(Entity(nextId++, null,
                (Ei, Info("Footprint", "fp" + i)),
                (Box, BoxBounds(-w / 2, -h / 2, w, h, passive: true)), (Xf, Transform(i * 64, 128))));
        }
        // A couple of convex props (visual → convex child).
        for (var i = 0; i < 3; i++)
            scene.Entities.Add(Entity(nextId++, null,
                (Ei, Info("Prop", "rock" + i)), (Body_Key, Body()),
                (Convx, Convex(new[] { new[] { -5.01f, 6.45f }, new[] { 1.5f, 4.46f }, new[] { 5.78f, 5.05f }, new[] { 0.23f, 4.57f } })),
                (Xf, Transform(300 + i * 40, -12))));

        AssertMigrateLoadSaveFixedPoint(V1Json(scene));
    }

    private static void AssertMigrateLoadSaveFixedPoint(string v1Json)
    {
        var migrated = ColliderMigration.Migrate(v1Json, "fixture.mdscene").Json; // collider lift → v2

        var fake = new InMemoryPlatform();
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            const string path = "Levels/fixture.mdscene";
            fake.Files[path] = migrated;

            var registry = new ComponentSerializerRegistry();
            registry.RegisterEngineComponents();
            var serializer = new SceneSerializer(registry);

            using var world = new World();
            using var reader = new SceneReaderSystem(world, serializer, content: null!, loadTexture: _ => null!);
            world.Publish(new LoadSceneRequest(path, fromContent: false));

            var resaved = CanonicalJson.Serialize(new SceneWriter(serializer).BuildScene(world));

            // pre-mortem #3: migrate → load → save is a byte fixed point MODULO the version bump. The
            // collider lift stamps v2; loading a clean v2 (no camera block) re-saves at the current version
            // (v3, per the CM guard). Everything but the version line is a fixed point — the reshaped
            // colliders + entity ordering round-trip. (TODO(CM-C): the umbrella `monodreams migrate`
            // produces v3 directly, making this a strict byte fixed point again.)
            var migratedAtCurrentVersion = CanonicalJson.Serialize(BumpVersion(migrated, SceneVersionGuard.CurrentVersion));
            Assert.Equal(migratedAtCurrentVersion, resaved);
        }
        finally { PlatformServices.Current = previous; }
    }

    /// <summary>Deserializes <paramref name="json"/>, stamps <paramref name="version"/>, and returns the
    /// scene — so the only intended difference from the input is the version line.</summary>
    private static SceneData BumpVersion(string json, int version)
    {
        var scene = CanonicalJson.Deserialize<SceneData>(json)!;
        scene.Version = version;
        return scene;
    }

    // ---- Directory recursion + dry-run + per-file summary (the CLI's file orchestration) ----

    [Fact]
    public void MigrateDirectory_RecursesAndMigrates_DryRunLeavesFilesUntouched()
    {
        var dir = Path.Combine(Path.GetTempPath(), "md-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        try
        {
            var sceneA = new SceneData();
            sceneA.Entities.Add(Entity(0, null, (Box, BoxBounds(-8, -8, 16, 16)), (Xf, Transform(0, 0))));
            var v1A = V1Json(sceneA);

            var sceneB = new SceneData(); // no colliders → still bumped to v2
            sceneB.Entities.Add(Entity(0, null, (Ei, Info("Prop", "p")), (Xf, Transform(1, 1))));
            var v1B = V1Json(sceneB);

            var already = new SceneData(); // already v2
            already.Entities.Add(Entity(0, null, (Ei, Info("Prop", "q")), (Xf, Transform(2, 2))));
            var v2C = CanonicalJson.Serialize(already);

            var pathA = Path.Combine(dir, "a.mdscene");
            var pathB = Path.Combine(dir, "nested", "b.mdprefab");
            var pathC = Path.Combine(dir, "c.mdscene");
            var notMd = Path.Combine(dir, "readme.txt");
            File.WriteAllText(pathA, v1A);
            File.WriteAllText(pathB, v1B);
            File.WriteAllText(pathC, v2C);
            File.WriteAllText(notMd, "ignore me");

            // Dry-run: reports the same changes but writes nothing.
            var dryReports = ColliderMigration.MigrateDirectory(dir, dryRun: true);
            Assert.Equal(3, dryReports.Count);                       // only .mdscene/.mdprefab, not .txt
            Assert.All(dryReports, r => Assert.False(r.Written));
            Assert.Equal(v1A, File.ReadAllText(pathA));              // untouched on disk
            Assert.Equal("ignore me", File.ReadAllText(notMd));

            // Real run: writes the changed files, leaves the already-v2 one alone.
            var reports = ColliderMigration.MigrateDirectory(dir, dryRun: false);
            var byName = reports.ToDictionary(r => Path.GetFileName(r.Path));
            Assert.True(byName["a.mdscene"].Result.Changed);
            Assert.Equal(1, byName["a.mdscene"].Result.BoxesReshapedInPlace);
            Assert.True(byName["b.mdprefab"].Result.Changed);        // version bumped even with no colliders
            Assert.True(byName["c.mdscene"].Result.AlreadyCurrent);
            Assert.False(byName["c.mdscene"].Result.Changed);

            Assert.Equal(2, CanonicalJson.Deserialize<SceneData>(File.ReadAllText(pathA))!.Version);
            Assert.Equal(2, CanonicalJson.Deserialize<SceneData>(File.ReadAllText(pathB))!.Version);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MigratePath_MissingPath_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ColliderMigration.MigratePath(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")), dryRun: false));
    }

    private const string Body_Key = "core.RigidBody";

    private sealed class InMemoryPlatform : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/mig/";
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) => Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }
}
