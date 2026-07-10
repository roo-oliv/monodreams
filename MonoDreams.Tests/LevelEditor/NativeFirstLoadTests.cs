using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
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
/// Protects PS4 <b>native-first level loading</b>: <c>LoadLevelRequest(id)</c> resolves a bundled native
/// <c>.mdscene</c> before the LDtk path (via <see cref="NativeLevelLoader"/> + the generalized
/// <see cref="SceneReaderSystem"/>), and the native reader serves the game boot with <b>no editor
/// composed</b> — a plain world + level-load systems. Pure logic — hand-built entities and an in-memory
/// platform; no real disk, no live <c>GraphicsDevice</c>.
///
/// Covers the level-editor premise "The game boots native scenes native-first via LoadLevelRequest".
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class NativeFirstLoadTests
{
    /// <summary>In-memory platform: WriteAllText stores into a dictionary that ReadAllText serves back,
    /// so a native <c>fromContent:false</c> read is a real JSON deserialize with no disk.</summary>
    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/native/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
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
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static List<Entity> CollectEntitiesWith<T>(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities()) list.Add(e);
        return list;
    }

    private sealed class TextureLoadSpy
    {
        public List<string> RequestedKeys { get; } = new();
        public Texture2D Load(string key) { RequestedKeys.Add(key); return null; }
    }

    /// <summary>Writes a tiny 2-entity native scene into <paramref name="fake"/> at the content-relative
    /// path a native <c>LoadLevelRequest(levelId)</c> resolves to.</summary>
    private static void WriteSampleScene(InMemoryPlatformServices fake, string levelId)
    {
        using var w = new World();
        for (var i = 0; i < 2; i++)
        {
            var e = w.CreateEntity();
            e.Set(new SceneObjectComponent());
            e.Set(new EntityInfoComponent("Prop", "p" + i));
            e.Set(new TransformComponent(new Vector2(i * 32, 0)));
            e.Set(new SpriteInfoComponent
            {
                AssetKey = "square",
                Size = new Vector2(64, 64),
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f,
            });
        }
        new SceneWriter(new SceneSerializer(NewEngineRegistry()))
            .Save(w, NativeLevelLoader.ContentRelativePath(levelId), camera: null, layers: null);
    }

    // ---- Native-first resolution: a bundled scene → the native reader builds the world (editor-free) ----

    [Fact]
    public void NativeFirst_LoadsScene_ViaTheNativeReader_WithNoEditorComposed()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            const string levelId = "sample";
            WriteSampleScene(fake, levelId);

            // A PLAIN world + a standalone native reader — no EditorOverlay, no editor systems.
            using var world = new World();
            var spy = new TextureLoadSpy();
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: spy.Load);

            // The native-first probe (what LevelLoadRequestSystem calls per LoadLevelRequest). exists=true
            // (the bundled file is present); fromContent:false so the reader reads the in-memory platform.
            var rel = NativeLevelLoader.ContentRelativePath(levelId);
            var probe = NativeLevelLoader.CreateProbe(world, "Content",
                exists: _ => fake.Files.ContainsKey(rel), fromContent: false);

            // Resolve the level id native-first: probe → LoadSceneRequest → reader builds the world.
            Assert.True(probe(levelId));

            // The entities + their components appear (reconstructed from components, editor-free).
            var loaded = CollectEntitiesWith<TransformComponent>(world);
            Assert.Equal(2, loaded.Count);
            Assert.All(loaded, e => Assert.True(e.Has<SpriteInfoComponent>()));
            Assert.All(loaded, e => Assert.True(e.Has<EntityInfoComponent>()));
            Assert.Contains("square", spy.RequestedKeys); // texture rehydrated from the AssetKey
        });
    }

    // ---- No native file → the probe returns false (the caller falls through to LDtk/Blender) ----

    [Fact]
    public void NoNativeScene_ProbeReturnsFalse_AndPublishesNothing()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var loadRequests = 0;
            world.Subscribe((in LoadSceneRequest _) => loadRequests++);

            var probe = NativeLevelLoader.CreateProbe(world, "Content",
                exists: _ => false, fromContent: false); // no bundled scene for this id

            Assert.False(probe("Level_0"));       // → the caller runs the LDtk path unchanged
            Assert.False(probe("Blender_Level")); // → no bundled scene in this synthetic root, so false
            Assert.Equal(0, loadRequests);        // nothing published, so nothing is loaded / clobbered
        });
    }

    // ---- The probe publishes a well-formed LoadSceneRequest for the content-relative scene path ----

    [Fact]
    public void Probe_PublishesLoadSceneRequest_ForContentRelativePath()
    {
        using var world = new World();
        LoadSceneRequest captured = default;
        var count = 0;
        world.Subscribe((in LoadSceneRequest m) => { captured = m; count++; });

        var probe = NativeLevelLoader.CreateProbe(world, "Content", exists: _ => true); // default fromContent:true

        Assert.True(probe("island"));
        Assert.Equal(1, count);
        Assert.Equal(Path.Combine("Levels", "island.mdscene"), captured.Path);
        Assert.True(captured.FromContent); // bundled content read (TitleContainer) in production
    }

    // ---- Source-first (UX-D pre-mortem #5): a resolved editor context makes the probe read the SOURCE
    //      tree; an unresolved context keeps the bundled path byte-identical ----

    /// <summary>A resolved editor context rooted at <c>/proj</c> (env var → an in-memory manifest under
    /// <c>Content/</c>), so <c>LevelsPath == /proj/Content/Levels</c> — mirrors OptionalSceneLoadTests.</summary>
    private static EditorProjectContext ResolvedContext()
    {
        const string root = "/proj";
        var manifestPath = Path.Combine(root, "Content", GameProject.FileName);
        var manifestJson = CanonicalJson.Serialize(new GameProject { StartScene = "island" });
        return EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/somewhere", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: name => name == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => manifestJson);
    }

    /// <summary>Writes a 1-entity native scene whose sole prop sits at <paramref name="pos"/>, to
    /// <paramref name="path"/> in <paramref name="fake"/> — so a reload's position identifies WHICH file
    /// was read.</summary>
    private static void WriteMarkerScene(InMemoryPlatformServices fake, string path, Vector2 pos)
    {
        using var w = new World();
        var e = w.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Prop", "marker"));
        e.Set(new TransformComponent(pos));
        // The writer routes through PlatformServices.Current (the ambient fake set by WithPlatform).
        new SceneWriter(new SceneSerializer(NewEngineRegistry())).Save(w, path, camera: null, layers: null);
    }

    [Fact]
    public void Probe_WithResolvedContext_ResolvesSourceFirst_PublishingTheSourcePath()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var ctx = ResolvedContext();
            var sourcePath = Path.Combine(ctx.LevelsPath!, "island" + SceneWriter.SceneFileExtension);

            using var world = new World();
            LoadSceneRequest captured = default;
            var count = 0;
            world.Subscribe((in LoadSceneRequest m) => { captured = m; count++; });

            // Source exists; the bundled copy also "exists" — source-first must still win.
            var probe = NativeLevelLoader.CreateProbe(world, "Content", ctx,
                exists: _ => true, fromContent: false, sourceExists: p => p == sourcePath);

            Assert.True(probe("island"));
            Assert.Equal(1, count);
            Assert.Equal(sourcePath, captured.Path);   // the SOURCE path, not the content-relative one
            Assert.False(captured.FromContent);          // fromContent:false = a source-tree read
        });
    }

    [Fact]
    public void Probe_WithNullContext_SkipsSourceFirst_AndUsesTheBundledPathUnchanged()
    {
        using var world = new World();
        LoadSceneRequest captured = default;
        var count = 0;
        world.Subscribe((in LoadSceneRequest m) => { captured = m; count++; });

        // Null context → the source branch is never entered (sourceExists must not be probed).
        var probe = NativeLevelLoader.CreateProbe(world, "Content", projectContext: null,
            exists: _ => true, sourceExists: _ => throw new InvalidOperationException("source must not be probed when unresolved"));

        Assert.True(probe("island"));
        Assert.Equal(1, count);
        Assert.Equal(NativeLevelLoader.ContentRelativePath("island"), captured.Path); // bundled, unchanged
        Assert.True(captured.FromContent);
    }

    /// <summary>
    /// The stale-bundle regression (UX-D pre-mortem #5): the bundled copy holds OLD bytes, the source tree
    /// holds NEW bytes (an editor Save that has not been re-bundled yet). A Restart re-publishes
    /// <c>LoadLevelRequest</c> through the probe → with a RESOLVED context the world must reflect the
    /// SOURCE (the last save); with an UNRESOLVED context it falls back to the bundled copy. Driven through
    /// the real <see cref="SceneReaderSystem"/> so the loaded world's marker position proves which file won.
    /// </summary>
    [Fact]
    public void StaleBundleRegression_ResolvedContextLoadsSource_UnresolvedLoadsBundled()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var ctx = ResolvedContext();
            var sourcePath = Path.Combine(ctx.LevelsPath!, "island" + SceneWriter.SceneFileExtension);
            var bundledRel = NativeLevelLoader.ContentRelativePath("island"); // "Levels/island.mdscene"

            var newPos = new Vector2(999, 1); // the last SAVE (source tree)
            var oldPos = new Vector2(7, 7);    // the last BUILD (stale bundle)
            WriteMarkerScene(fake, sourcePath, newPos);
            WriteMarkerScene(fake, bundledRel, oldPos);

            // Resolved context → source-first → the world reflects the SOURCE (NEW) bytes.
            using (var world = new World())
            {
                using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                    content: null, loadTexture: _ => null);
                var probe = NativeLevelLoader.CreateProbe(world, "Content", ctx,
                    exists: _ => true, fromContent: false, sourceExists: p => p == sourcePath);
                Assert.True(probe("island"));
                var loaded = CollectEntitiesWith<TransformComponent>(world);
                Assert.Single(loaded);
                Assert.Equal(newPos, loaded[0].Get<TransformComponent>().Position); // SOURCE won
            }

            // Unresolved context → bundled → the world reflects the (stale) BUNDLE (OLD) bytes.
            using (var world = new World())
            {
                using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                    content: null, loadTexture: _ => null);
                var probe = NativeLevelLoader.CreateProbe(world, "Content", projectContext: null,
                    exists: _ => true, fromContent: false);
                Assert.True(probe("island"));
                var loaded = CollectEntitiesWith<TransformComponent>(world);
                Assert.Single(loaded);
                Assert.Equal(oldPos, loaded[0].Get<TransformComponent>().Position); // BUNDLE (byte-identical path)
            }
        });
    }

    // ---- The committed Examples sample.mdscene is byte-locked to the canonical serializer output ----

    /// <summary>
    /// Reconstructs the exact 2-entity world the committed
    /// <c>MonoDreams.Examples.Core/Content/Levels/sample.mdscene</c> is generated from and asserts the
    /// canonical bytes match it — so the committed sample stays a byte fixed point (drift would break the
    /// game boot test). The committed file is generated FROM this serializer, so they are identical.
    /// </summary>
    [Fact]
    public void CommittedSampleScene_MatchesTheCanonicalShape()
    {
        using var world = new World();
        Make(world, "grass", new Vector2(0, 0));
        Make(world, "stone", new Vector2(96, 32));

        var json = CanonicalJson.Serialize(
            new SceneWriter(new SceneSerializer(NewEngineRegistry())).BuildScene(world));

        Assert.Equal(ExpectedSampleScene, json);
        return;

        static void Make(World w, string name, Vector2 pos)
        {
            var e = w.CreateEntity();
            e.Set(new SceneObjectComponent());
            e.Set(new EntityInfoComponent("Prop", name));
            e.Set(new TransformComponent(pos));
            e.Set(new SpriteInfoComponent
            {
                AssetKey = "square",
                Source = new Rectangle(0, 0, 32, 32),
                Size = new Vector2(64, 64),
                Color = Color.White,
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f,
            });
        }
    }

    // ---- The committed sample bytes load back via the native reader (editor-free) ----

    [Fact]
    public void CommittedSampleScene_LoadsBackViaTheNativeReader()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            const string path = "Levels/sample.mdscene";
            fake.Files[path] = ExpectedSampleScene;

            using var world = new World();
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: new TextureLoadSpy().Load);
            world.Publish(new LoadSceneRequest(path, fromContent: false));

            var loaded = CollectEntitiesWith<SpriteInfoComponent>(world);
            Assert.Equal(2, loaded.Count);
            Assert.Contains(loaded, e => e.Get<EntityInfoComponent>().Name == "grass");
            Assert.Contains(loaded, e => e.Get<EntityInfoComponent>().Name == "stone");
            Assert.All(loaded, e => Assert.Equal("square", e.Get<SpriteInfoComponent>().AssetKey));
        });
    }

    /// <summary>The exact canonical bytes committed as the Examples sample scene (kept in sync with
    /// <c>MonoDreams.Examples.Core/Content/Levels/sample.mdscene</c> — both come from the same serializer).</summary>
    private const string ExpectedSampleScene =
        "{\n" +
        "  \"version\": 2,\n" +
        "  \"layers\": [],\n" +
        "  \"sources\": [],\n" +
        "  \"entities\": [\n" +
        "    {\n" +
        "      \"id\": 0,\n" +
        "      \"components\": {\n" +
        "        \"core.EntityInfo\": {\n" +
        "          \"type\": \"Prop\",\n" +
        "          \"name\": \"grass\"\n" +
        "        },\n" +
        "        \"core.SpriteInfo\": {\n" +
        "          \"assetKey\": \"square\",\n" +
        "          \"source\": [\n" +
        "            0,\n" +
        "            0,\n" +
        "            32,\n" +
        "            32\n" +
        "          ],\n" +
        "          \"size\": [\n" +
        "            64,\n" +
        "            64\n" +
        "          ],\n" +
        "          \"color\": \"/////w==\",\n" +
        "          \"origin\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ],\n" +
        "          \"offset\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ],\n" +
        "          \"target\": 0,\n" +
        "          \"layerDepth\": 0.5,\n" +
        "          \"ySortOffset\": 0,\n" +
        "          \"ySortDepthBias\": 0\n" +
        "        },\n" +
        "        \"core.Transform\": {\n" +
        "          \"position\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ],\n" +
        "          \"rotation\": 0,\n" +
        "          \"scale\": [\n" +
        "            1,\n" +
        "            1\n" +
        "          ],\n" +
        "          \"origin\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ]\n" +
        "        }\n" +
        "      }\n" +
        "    },\n" +
        "    {\n" +
        "      \"id\": 1,\n" +
        "      \"components\": {\n" +
        "        \"core.EntityInfo\": {\n" +
        "          \"type\": \"Prop\",\n" +
        "          \"name\": \"stone\"\n" +
        "        },\n" +
        "        \"core.SpriteInfo\": {\n" +
        "          \"assetKey\": \"square\",\n" +
        "          \"source\": [\n" +
        "            0,\n" +
        "            0,\n" +
        "            32,\n" +
        "            32\n" +
        "          ],\n" +
        "          \"size\": [\n" +
        "            64,\n" +
        "            64\n" +
        "          ],\n" +
        "          \"color\": \"/////w==\",\n" +
        "          \"origin\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ],\n" +
        "          \"offset\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ],\n" +
        "          \"target\": 0,\n" +
        "          \"layerDepth\": 0.5,\n" +
        "          \"ySortOffset\": 0,\n" +
        "          \"ySortDepthBias\": 0\n" +
        "        },\n" +
        "        \"core.Transform\": {\n" +
        "          \"position\": [\n" +
        "            96,\n" +
        "            32\n" +
        "          ],\n" +
        "          \"rotation\": 0,\n" +
        "          \"scale\": [\n" +
        "            1,\n" +
        "            1\n" +
        "          ],\n" +
        "          \"origin\": [\n" +
        "            0,\n" +
        "            0\n" +
        "          ]\n" +
        "        }\n" +
        "      }\n" +
        "    }\n" +
        "  ]\n" +
        "}\n";
}
