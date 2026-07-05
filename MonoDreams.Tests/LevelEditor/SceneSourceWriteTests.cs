using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects project-persistence <b>PS3</b>: the editor's Save writes a versioned
/// <c>&lt;id&gt;.mdscene</c> into the <b>project SOURCE tree</b> (<c>ProjectRoot/LevelsDir</c>) via
/// <c>IPlatformServices.WriteAllText</c> — <b>not</b> the ephemeral build-output <c>BaseDirectory</c>
/// (the pre-PS3 <c>ExportScene</c> path) — the scene id defaults from the manifest's
/// <see cref="GameProject.StartScene"/>, the write is refused (defense-in-depth) when the project is
/// unresolved, and the in-editor Load reads that same source file back so <c>load → edit → save</c>
/// stays the byte-stable fixed point (PS1) at the new location.
///
/// <para>The writer/reader route through the process-global <see cref="PlatformServices.Current"/>,
/// so this class is in the non-parallel collection and restores the default.</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class SceneSourceWriteTests
{
    private const string EnvRoot = "/proj";

    /// <summary>In-memory platform: WriteAllText / ReadAllText share a dictionary (the source-tree
    /// write and the reload read hit the same store) and FileExists answers the context resolution.
    /// <see cref="ExportScene"/> is the retired out-of-band seam — kept only so a call to it would be
    /// visible (it must never fire on the PS3 path).</summary>
    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public HashSet<string> CreatedDirectories { get; } = new();
        public int ExportCount { get; private set; }
        public int WriteCount { get; private set; }
        public string BaseDirectory => "/proj/bin/Debug/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => Path.Combine(paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { Files[path] = contents; WriteCount++; }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents)
        {
            ExportCount++;
            Files[suggestedFileName] = contents;
            return suggestedFileName;
        }
        public void CreateDirectory(string path) => CreatedDirectories.Add(path);
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    /// <summary>A resolved context anchored at <see cref="EnvRoot"/> (env var → an in-memory manifest
    /// under <c>Content/</c>), with the given <paramref name="startScene"/> — so
    /// <c>LevelsPath == /proj/Content/Levels</c>.</summary>
    private static EditorProjectContext ResolvedContext(string startScene = "island")
    {
        var manifestPath = Path.Combine(EnvRoot, "Content", GameProject.FileName);
        var manifestJson = CanonicalJson.Serialize(new GameProject { StartScene = startScene });
        return EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/proj", "bin", "Debug") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: name => name == EditorProjectContext.ProjectRootVariable ? EnvRoot : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => manifestJson);
    }

    /// <summary>A tagged save-root sprite (with an AssetKey, so rehydration is exercised on reload).</summary>
    private static Entity MakeRoot(World world, Vector2 position)
    {
        var e = world.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Player", "Hero"));
        e.Set(new TransformComponent(position));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Player",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        return e;
    }

    // ---- Save writes to ProjectRoot/LevelsDir/<id>.mdscene with canonical bytes, NOT BaseDirectory ----

    [Fact]
    public void Save_WritesIntoTheProjectSourceTree_NotBaseDirectory()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            var ctx = ResolvedContext();

            MakeRoot(world, new Vector2(10, 20));

            var sceneId = EditorOverlay.ResolveSceneId(null, ctx); // "island" from the manifest
            var path = EditorOverlay.SceneFilePath(ctx, sceneId);
            Assert.Equal(Path.Combine(EnvRoot, "Content", "Levels", "island.mdscene"), path);

            var written = new SceneWriter(serializer).Save(world, path, camera: null, layers: null);

            // Landed at the resolved SOURCE path, with the exact canonical bytes.
            Assert.Equal(path, written);
            Assert.True(fake.Files.ContainsKey(path!));
            var expected = CanonicalJson.Serialize(new SceneWriter(serializer).BuildScene(world));
            Assert.Equal(expected, fake.Files[path!]);

            // The levels directory was ensured; the write went through WriteAllText, never ExportScene.
            Assert.Contains(Path.Combine(EnvRoot, "Content", "Levels"), fake.CreatedDirectories);
            Assert.Equal(1, fake.WriteCount);
            Assert.Equal(0, fake.ExportCount);

            // Nothing landed under the build-output BaseDirectory (the retired pre-PS3 target).
            Assert.DoesNotContain(fake.Files.Keys, k => k.StartsWith(fake.BaseDirectory, StringComparison.Ordinal));
            Assert.All(fake.Files.Keys, k =>
                Assert.StartsWith(Path.Combine(EnvRoot, "Content", "Levels"), k));
        });
    }

    // ---- Save is refused (loud, no write) when the project is unresolved (defense in depth) ----

    [Fact]
    public void Save_Refused_WhenProjectUnresolved()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            MakeRoot(world, Vector2.Zero);

            // The overlay would produce a null path for an unresolved project…
            Assert.Null(EditorOverlay.SceneFilePath(EditorProjectContext.Unresolved, "island"));
            Assert.Null(EditorOverlay.SceneFilePath(null, "island"));

            // …and the writer's own guard refuses a null/empty path with no write (backstop).
            Assert.Null(new SceneWriter(serializer).Save(world, EditorOverlay.SceneFilePath(EditorProjectContext.Unresolved, "island")));
            Assert.Null(new SceneWriter(serializer).Save(world, null));
            Assert.Null(new SceneWriter(serializer).Save(world, ""));

            Assert.Empty(fake.Files);
            Assert.Equal(0, fake.WriteCount);
        });
    }

    // ---- Named scene: the id defaults from the manifest startScene, else "untitled"; explicit wins ----

    [Fact]
    public void SceneId_DefaultsFromManifestStartScene_ElseUntitled()
    {
        Assert.Equal("island", EditorOverlay.ResolveSceneId(null, ResolvedContext("island")));
        Assert.Equal("world_1", EditorOverlay.ResolveSceneId(null, ResolvedContext("world_1")));

        // No project, or a resolved project with an empty startScene → the "untitled" fallback.
        Assert.Equal(EditorOverlay.DefaultSceneId, EditorOverlay.ResolveSceneId(null, null));
        Assert.Equal(EditorOverlay.DefaultSceneId, EditorOverlay.ResolveSceneId(null, EditorProjectContext.Unresolved));
        Assert.Equal(EditorOverlay.DefaultSceneId, EditorOverlay.ResolveSceneId(null, ResolvedContext(startScene: "")));

        // An explicit id always wins (over the manifest and the fallback).
        Assert.Equal("custom", EditorOverlay.ResolveSceneId("custom", ResolvedContext("island")));
        Assert.Equal("custom", EditorOverlay.ResolveSceneId("custom", null));

        // The id becomes <id>.mdscene under the levels directory.
        Assert.Equal(Path.Combine(EnvRoot, "Content", "Levels", "custom.mdscene"),
            EditorOverlay.SceneFilePath(ResolvedContext(), "custom"));
    }

    // ---- The in-editor Load reads the just-written source file → the world round-trips ----

    [Fact]
    public void Load_ReadsTheJustWrittenSourceFile_RoundTrips()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var ctx = ResolvedContext();
            var path = EditorOverlay.SceneFilePath(ctx, EditorOverlay.ResolveSceneId(null, ctx))!;

            // Write from a source world.
            using (var writeWorld = new World())
            {
                var serializer = new SceneSerializer(NewEngineRegistry());
                MakeRoot(writeWorld, new Vector2(42, 7));
                new SceneWriter(serializer).Save(writeWorld, path);
            }
            Assert.True(fake.Files.ContainsKey(path));

            // Reload onto a FRESH world via LoadSceneRequest reading that same source path directly.
            using var loadWorld = new World();
            var requestedKeys = new List<string>();
            using var reader = new SceneReaderSystem(loadWorld, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: key => { requestedKeys.Add(key); return (Texture2D)null; });

            loadWorld.Publish(new LoadSceneRequest(path, fromContent: false));

            var roots = new List<Entity>();
            using (var set = loadWorld.GetEntities().With<SceneObjectComponent>().AsSet())
                foreach (var e in set.GetEntities()) roots.Add(e);

            Assert.Single(roots);
            Assert.Equal(new Vector2(42, 7), roots[0].Get<TransformComponent>().Position);
            Assert.Equal("Atlas/TX Player", roots[0].Get<SpriteInfoComponent>().AssetKey);
            Assert.Contains("Atlas/TX Player", requestedKeys); // rehydration ran off the source file
        });
    }

    // ---- save → reload → save at the SOURCE path is a byte-stable fixed point (PS1 through the repoint) ----

    [Fact]
    public void SaveReloadSave_AtTheSourcePath_IsAByteStableFixedPoint()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var ctx = ResolvedContext();
            var path = EditorOverlay.SceneFilePath(ctx, EditorOverlay.ResolveSceneId(null, ctx))!;

            using (var world1 = new World())
            {
                var serializer = new SceneSerializer(NewEngineRegistry());
                MakeRoot(world1, new Vector2(3, 4));
                MakeRoot(world1, new Vector2(90, 90));
                new SceneWriter(serializer).Save(world1, path);
            }
            var firstBytes = fake.Files[path];

            // Reload into a fresh world, then re-save to the SAME source path (overwrites in place).
            using var world2 = new World();
            using var reader = new SceneReaderSystem(world2, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: _ => (Texture2D)null);
            world2.Publish(new LoadSceneRequest(path, fromContent: false));
            new SceneWriter(new SceneSerializer(NewEngineRegistry())).Save(world2, path);

            // load → save equals the source file byte-for-byte (ids restored, ordering stable).
            Assert.Equal(firstBytes, fake.Files[path]);
        });
    }
}
