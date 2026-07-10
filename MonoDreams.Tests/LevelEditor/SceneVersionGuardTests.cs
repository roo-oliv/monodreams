#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the CE-B <b>scene-format version 2 gate</b> (<see cref="SceneVersionGuard"/>): a file read of a
/// version-1 scene/prefab that carries ANY collider component is refused loud (with the migrator hint —
/// pre-mortem #2), a version-1 file WITHOUT colliders loads and re-saves as version 2, a version-2 file
/// loads, and an in-memory snapshot is version-agnostic (never guarded). Pure logic — hand-built scenes
/// + the real reader; no <c>GraphicsDevice</c>.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class SceneVersionGuardTests
{
    private const string Box = EngineComponentSerializers.BoxColliderKey;
    private const string Convx = EngineComponentSerializers.ConvexColliderKey;
    private const string Xf = EngineComponentSerializers.TransformKey;
    private const string Ei = EngineComponentSerializers.EntityInfoKey;

    private static JsonElement El(object v) => CanonicalJson.SerializeToElement(v);
    private static JsonElement Transform(float x, float y) =>
        El(new { position = new[] { x, y }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
    private static JsonElement Info(string type, string name) => El(new { type, name });
    private static JsonElement BoxSize(float w, float h) =>
        El(new { size = new[] { w, h }, activeLayers = new[] { -1 }, passive = false, enabled = true });

    private static SceneData Scene(int version, params SceneEntityData[] entities)
    {
        var s = new SceneData { Version = version };
        s.Entities.AddRange(entities);
        return s;
    }

    private static SceneEntityData Entity(int? id, params (string Key, JsonElement Value)[] comps)
    {
        var e = new SceneEntityData { Id = id };
        foreach (var (k, v) in comps) e.Components[k] = v;
        return e;
    }

    private static ComponentSerializerRegistry Registry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    // ---- Unit: the guard itself ----

    [Fact]
    public void Guard_V1WithBoxCollider_Refuses_WithMigratorHint()
    {
        var scene = Scene(1, Entity(0, (Box, BoxSize(16, 16)), (Xf, Transform(0, 0))));
        var ex = Assert.Throws<InvalidOperationException>(() => SceneVersionGuard.CheckFileLoad(scene, "legacy.mdscene"));
        Assert.Contains("legacy embedded colliders", ex.Message);
        Assert.Contains("monodreams migrate-colliders", ex.Message);
        Assert.Contains("legacy.mdscene", ex.Message);
    }

    [Fact]
    public void Guard_V1WithConvexCollider_Refuses()
    {
        var convex = El(new
        {
            modelVertices = new[] { new[] { 0f, 0f }, new[] { 4f, 0f }, new[] { 0f, 4f } },
            activeLayers = new[] { -1 }, passive = false, enabled = true, ignoreTransformRotation = false,
        });
        var scene = Scene(1, Entity(0, (Convx, convex), (Xf, Transform(0, 0))));
        Assert.Throws<InvalidOperationException>(() => SceneVersionGuard.CheckFileLoad(scene, "legacy.mdscene"));
    }

    [Fact]
    public void Guard_V1WithoutColliders_Passes()
    {
        var scene = Scene(1, Entity(0, (Ei, Info("Prop", "p")), (Xf, Transform(1, 2))));
        SceneVersionGuard.CheckFileLoad(scene, "clean.mdscene"); // no throw
    }

    [Fact]
    public void Guard_V2WithColliders_Passes()
    {
        var scene = Scene(2, Entity(0, (Box, BoxSize(16, 16)), (Xf, Transform(0, 0))));
        SceneVersionGuard.CheckFileLoad(scene, "v2.mdscene"); // no throw
    }

    // ---- Integration: the reader's FILE path applies the guard ----

    [Fact]
    public void Reader_V1FileWithColliders_FailsLoud_WithMigratorHint()
    {
        WithPlatform(fake =>
        {
            const string path = "Levels/legacy.mdscene";
            fake.Files[path] = CanonicalJson.Serialize(Scene(1, Entity(0, (Box, BoxSize(16, 16)), (Xf, Transform(0, 0)))));

            using var world = new World();
            using var reader = new SceneReaderSystem(world, new SceneSerializer(Registry()), content: null!, loadTexture: _ => null!);

            var ex = Assert.Throws<InvalidOperationException>(() => world.Publish(new LoadSceneRequest(path, fromContent: false)));
            Assert.Contains("monodreams migrate-colliders", ex.Message);

            // Nothing was reconstructed — the refusal aborts before any entity is created.
            using var set = world.GetEntities().With<TransformComponent>().AsSet();
            Assert.Empty(set.GetEntities().ToArray());
        });
    }

    [Fact]
    public void Reader_V1CleanFile_Loads_AndReSavesAsVersion2()
    {
        WithPlatform(fake =>
        {
            const string path = "Levels/clean.mdscene";
            fake.Files[path] = CanonicalJson.Serialize(Scene(1, Entity(0, (Ei, Info("Prop", "p")), (Xf, Transform(3, 4)))));

            using var world = new World();
            var registry = Registry();
            using var reader = new SceneReaderSystem(world, new SceneSerializer(registry), content: null!, loadTexture: _ => null!);
            world.Publish(new LoadSceneRequest(path, fromContent: false)); // no throw — clean v1 loads

            using (var set = world.GetEntities().With<TransformComponent>().AsSet())
                Assert.Single(set.GetEntities().ToArray());

            // Re-saving the loaded world stamps the current version (2) — the "v1 clean re-saves as v2" rule.
            var resaved = CanonicalJson.Deserialize<SceneData>(
                CanonicalJson.Serialize(new SceneWriter(new SceneSerializer(registry)).BuildScene(world)))!;
            Assert.Equal(2, resaved.Version);
        });
    }

    [Fact]
    public void Reader_InMemorySnapshot_IsVersionAgnostic_NotGuarded()
    {
        // A Game-mode in-memory snapshot restore (LoadSceneRequest(SceneData)) is NEVER guarded — even a
        // hand-crafted version-1 SceneData carrying a collider restores, because it was produced live this
        // session (never read off disk), so it cannot carry a legacy on-disk shape.
        WithPlatform(_ =>
        {
            var snapshot = Scene(1, Entity(0, (Box, BoxSize(16, 16)), (Xf, Transform(0, 0))));

            using var world = new World();
            using var reader = new SceneReaderSystem(world, new SceneSerializer(Registry()), content: null!, loadTexture: _ => null!);
            world.Publish(new LoadSceneRequest(snapshot)); // in-memory: no throw despite version 1 + collider

            using var set = world.GetEntities().With<BoxColliderComponent>().AsSet();
            Assert.Single(set.GetEntities().ToArray());
        });
    }

    private static void WithPlatform(Action<InMemoryPlatform> body)
    {
        var fake = new InMemoryPlatform();
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(fake); }
        finally { PlatformServices.Current = previous; }
    }

    private sealed class InMemoryPlatform : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/guard/";
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
