#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the CM <b>umbrella migrator</b> (<see cref="SceneMigration"/>, the engine core behind
/// <c>monodreams migrate</c>): it applies every lift in version order (v1→v2 colliders, then v2→v3 camera),
/// so a v1 file goes <b>straight to v3</b> in one pass; it is idempotent (a v3 file is a no-op); and — the
/// strict form of CE pre-mortem #3 — a <c>migrate → load → save</c> through the umbrella is a <b>strict</b>
/// byte fixed point (the single collider lift is only a fixed point modulo the version line — see
/// <see cref="ColliderMigrationTests"/>). Pure logic — hand-built canonical v1 fixtures + the real
/// reader/writer round-trip; no <c>GraphicsDevice</c>, no user files.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class SceneMigrationTests
{
    private static JsonElement El(object value) => CanonicalJson.SerializeToElement(value);
    private static JsonElement Transform(float x, float y) =>
        El(new { position = new[] { x, y }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
    private static JsonElement Info(string type, string name) => El(new { type, name });
    private static JsonElement Body() => El(new
    {
        mass = 1f, gravityActive = true, gravityFactor = 1f, isKinematic = false,
        freezeRotation = false, freezePositionX = false, freezePositionY = false,
    });
    private static JsonElement BoxBounds(int x, int y, int w, int h) =>
        El(new { bounds = new[] { x, y, w, h }, activeLayers = new[] { -1 }, passive = true, enabled = true });

    private const string Box = "core.BoxCollider";
    private const string Cam = "core.Camera";
    private const string Xf = "core.Transform";
    private const string Ei = "core.EntityInfo";

    private static SceneEntityData Entity(int? id, int? parent, params (string Key, JsonElement Value)[] comps)
    {
        var e = new SceneEntityData { Id = id, Parent = parent };
        foreach (var (k, v) in comps) e.Components[k] = v;
        return e;
    }

    // ---- A v1 file with an embedded collider AND a camera block goes straight to v3 in one pass ----

    [Fact]
    public void Umbrella_V1_MigratesStraightToV3_BothLiftsRun()
    {
        var scene = new SceneData { Version = 1 };
        scene.Camera = new SceneCameraData { Position = new[] { 100f, 50f }, Zoom = 2f, Rotation = 0.5f };
        // A body entity with an embedded box (the collider lift moves it to a child).
        scene.Entities.Add(Entity(0, null, (Ei, Info("Wall", "wall")), ("core.RigidBody", Body()),
            (Box, BoxBounds(-8, -8, 16, 16)), (Xf, Transform(10, 20))));
        var v1 = CanonicalJson.Serialize(scene);

        var result = SceneMigration.Migrate(v1, "level.mdscene");

        Assert.True(result.Changed);
        Assert.False(result.AlreadyCurrent);
        Assert.True(result.Collider.Changed);   // v1→v2 collider lift ran
        Assert.True(result.Camera.Changed);      // v2→v3 camera lift ran
        Assert.True(result.Camera.CameraBlockLifted);
        Assert.Equal(2, result.LiftsApplied.Count);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(3, migrated.Version);
        Assert.Null(migrated.Camera);
        // The box moved to a child collider entity …
        Assert.False(migrated.Entities[0].Components.ContainsKey(Box));
        Assert.Contains(migrated.Entities, e => e.Parent == 0 && e.Components.ContainsKey(Box));
        // … and the camera block became a camera entity (sorts last, id = max root id + 1).
        var cam = migrated.Entities.Single(e => e.Components.ContainsKey(Cam));
        Assert.Equal(2f, cam.Components[Cam].GetProperty("zoom").GetSingle(), 4);
        Assert.Equal(new[] { 100f, 50f }, cam.Components[Xf].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray());
    }

    // ---- Idempotence: a v3 file is a byte-identical no-op ----

    [Fact]
    public void Umbrella_AlreadyV3_IsANoOp()
    {
        var scene = new SceneData(); // v3
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "p")), (Xf, Transform(1, 2))));
        var v3 = CanonicalJson.Serialize(scene);

        var result = SceneMigration.Migrate(v3, "current.mdscene");
        Assert.True(result.AlreadyCurrent);
        Assert.False(result.Changed);
        Assert.Equal(v3, result.Json);
        Assert.Empty(result.LiftsApplied);
    }

    // ---- The strict byte fixed point: migrate → load → save == migrate (no version modulo) ----

    [Fact]
    public void Umbrella_MigrateLoadSave_IsAStrictByteFixedPoint()
    {
        var scene = new SceneData { Version = 1 };
        scene.Camera = new SceneCameraData { Position = new[] { 208f, 108f }, Zoom = 1.75f, Rotation = 0.25f };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Player", "pete")), ("core.RigidBody", Body()),
            (Box, BoxBounds(-8, -18, 16, 36)), (Xf, Transform(48, 20))));
        scene.Entities.Add(Entity(1, null, (Ei, Info("Prop", "rock")), (Xf, Transform(300, -12))));

        AssertUmbrellaMigrateLoadSaveIsStrictFixedPoint(CanonicalJson.Serialize(scene));
    }

    [Fact]
    public void Umbrella_CameraLessV1_MigrateLoadSave_IsAStrictByteFixedPoint()
    {
        // No camera block: the camera lift adds the default camera at the origin; the fixed point still holds.
        var scene = new SceneData { Version = 1 };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "grass")), (Xf, Transform(0, 0))));
        scene.Entities.Add(Entity(1, null, (Ei, Info("Wall", "wall")), ("core.RigidBody", Body()),
            (Box, BoxBounds(-8, -12, 16, 24)), (Xf, Transform(200, 0))));

        AssertUmbrellaMigrateLoadSaveIsStrictFixedPoint(CanonicalJson.Serialize(scene));
    }

    private static void AssertUmbrellaMigrateLoadSaveIsStrictFixedPoint(string v1Json)
    {
        var migrated = SceneMigration.Migrate(v1Json, "fixture.mdscene").Json; // full chain → v3

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
            // ensureSingleCamera is irrelevant here: the migrated v3 file already carries a camera entity, so
            // the ensure would be a no-op anyway. Load restores it (id preserved), the writer re-emits it.
            using var reader = new SceneReaderSystem(world, serializer, content: null!, loadTexture: _ => null!);
            world.Publish(new LoadSceneRequest(path, fromContent: false));

            var resaved = CanonicalJson.Serialize(new SceneWriter(serializer).BuildScene(world));

            Assert.Equal(migrated, resaved);                        // STRICT byte fixed point (v3 == v3)
            Assert.Equal(3, CanonicalJson.Deserialize<SceneData>(migrated)!.Version);
        }
        finally { PlatformServices.Current = previous; }
    }

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
