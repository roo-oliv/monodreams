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
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave 3 scene round-trip: the writer (membership closure + camera/layers +
/// export through <see cref="IPlatformServices"/>) and the reader (<see cref="LoadSceneRequest"/>
/// two-pass create + deserialize + parent-wire + <c>Texture2D</c> rehydration), built on the
/// Wave-2 <see cref="SceneSerializer"/>. Pure logic — hand-built entities and a fake platform; no
/// real disk and no live <c>GraphicsDevice</c> (the texture loader is a stub, so we assert the
/// rehydration call + asset key rather than pixel data, per the plan).
///
/// Covers the level-editor premise "Scene round-trip reconstructs from registered components, not
/// factories" via the named tests <c>SceneRoundTripGoldenTest</c>, <c>MembershipFilterTest</c>,
/// <c>DerivedDepthReproductionTest</c>.
///
/// The writer/reader route through the process-global <see cref="PlatformServices.Current"/> and
/// <see cref="Logger"/>, so this class is in the non-parallel collection and restores the defaults.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class SceneRoundTripTests
{
    private const string SceneFileName = "round-trip.scene.json";

    /// <summary>In-memory platform: ExportScene stores into a dictionary that ReadAllText serves back,
    /// so the writer→reader file hop is a real JSON serialize/deserialize with no disk.</summary>
    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents)
        {
            // Reader uses fromContent:false → reads back via ReadAllText(path); export under the
            // same name so the path matches.
            Files[suggestedFileName] = contents;
            return suggestedFileName;
        }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            body();
        }
        finally
        {
            PlatformServices.Current = previous;
        }
    }

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    /// <summary>Materializes the entities in <paramref name="world"/> carrying <typeparamref name="T"/>
    /// into a list (EntitySet.GetEntities returns a span, which LINQ can't operate on directly).</summary>
    private static List<Entity> CollectEntitiesWith<T>(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities())
            list.Add(e);
        return list;
    }

    // A texture-load stub: no GraphicsDevice. Returns a sentinel (null is fine — we only need the
    // call to happen) and records which keys were requested so the test can assert rehydration ran.
    private sealed class TextureLoadSpy
    {
        public List<string> RequestedKeys { get; } = new();
        public Texture2D Load(string key)
        {
            RequestedKeys.Add(key);
            return null; // a real Texture2D needs a GraphicsDevice; the AssetKey + the call are what we assert
        }
    }

    // ---- SceneRoundTripGoldenTest: tag a sprite root + a child sub-graph, write, reload, compare ----

    [Fact]
    public void SceneRoundTripGoldenTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var sourceWorld = new World();
            var registry = NewEngineRegistry();
            var serializer = new SceneSerializer(registry);

            // Tagged root: a sprite entity with an AssetKey + full Transform + SOURCE sort fields.
            var root = sourceWorld.CreateEntity();
            root.Set(new SceneObjectComponent());
            root.Set(new EntityInfoComponent("Player", "Hero"));
            root.Set(new TransformComponent(new Vector2(12, 34), rotation: 0.7f,
                scale: new Vector2(2, 1.5f), origin: new Vector2(3, 4)));
            root.Set(new SpriteInfoComponent
            {
                AssetKey = "Atlas/TX Player",
                Source = new Rectangle(4, 8, 16, 32),
                Size = new Vector2(16, 32),
                Color = new Color(11, 22, 33, 44),
                Origin = new Vector2(2, 2),
                Offset = new Vector2(5, 6),
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f,
                YSortOffset = 9f,
                YSortDepthBias = 0.003f,
            });

            // Child sub-graph: ChildOf(root) — round-trips with its parent even though only root is tagged.
            var child = sourceWorld.CreateEntity();
            child.Set(new EntityInfoComponent("Orb", "BlueOrb"));
            child.Set(new TransformComponent(new Vector2(50, 0)));
            child.SetParent(root);

            // Write the scene with a camera + a layer map.
            var camera = new MonoDreams.Component.Camera();
            camera.Position = new Vector2(100, 200);
            camera.Zoom = 1.25f;
            var layers = DrawLayerMap.FromEnum<TestLayer>();
            var writer = new SceneWriter(serializer);
            var locator = writer.Save(sourceWorld, SceneFileName, camera, layers);

            Assert.Equal(SceneFileName, locator);
            Assert.True(fake.Files.ContainsKey(SceneFileName));

            // Reload onto a FRESH world via LoadSceneRequest (reads back through IPlatformServices).
            using var loadWorld = new World();
            var loadRegistry = NewEngineRegistry();
            var loadSerializer = new SceneSerializer(loadRegistry);
            var spy = new TextureLoadSpy();
            using var reader = new SceneReaderSystem(loadWorld, loadSerializer, content: null, loadTexture: spy.Load);

            loadWorld.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // The same tagged set reproduces: a sprite root + its child = 2 entities.
            var loaded = CollectEntitiesWith<TransformComponent>(loadWorld);
            Assert.Equal(2, loaded.Count);

            var loadedRoot = loaded.Single(e => e.Has<SpriteInfoComponent>());
            var loadedChild = loaded.Single(e => e.Has<ChildOfComponent>());

            // Transform reproduces (pos/rot/scale/origin).
            var t = loadedRoot.Get<TransformComponent>();
            Assert.Equal(new Vector2(12, 34), t.Position);
            Assert.Equal(0.7f, t.Rotation);
            Assert.Equal(new Vector2(2, 1.5f), t.Scale);
            Assert.Equal(new Vector2(3, 4), t.Origin);

            // SpriteInfo SOURCE sort fields + AssetKey reproduce; texture was rehydrated via the loader.
            var s = loadedRoot.Get<SpriteInfoComponent>();
            Assert.Equal("Atlas/TX Player", s.AssetKey);
            Assert.Equal(0.5f, s.LayerDepth);
            Assert.Equal(9f, s.YSortOffset);
            Assert.Equal(0.003f, s.YSortDepthBias);
            Assert.Equal(new Rectangle(4, 8, 16, 32), s.Source);
            Assert.Equal(new Color(11, 22, 33, 44), s.Color);
            Assert.Equal(new Vector2(5, 6), s.Offset);
            // Rehydration ran for the asset key (the load call is what we assert, not pixel data).
            Assert.Contains("Atlas/TX Player", spy.RequestedKeys);

            // Parent graph reproduces: child's ChildOf points at the loaded root, transforms are linked.
            Assert.True(loadedChild.Has<ChildOfComponent>());
            Assert.Equal(loadedRoot, loadedChild.Get<ChildOfComponent>().Parent);
            Assert.Same(loadedRoot.Get<TransformComponent>(), loadedChild.Get<TransformComponent>().Parent);

            // Camera + layers persisted into the file (reconstructable banding).
            var reloadedScene = global::System.Text.Json.JsonSerializer.Deserialize<SceneData>(fake.Files[SceneFileName]);
            Assert.NotNull(reloadedScene.Camera);
            Assert.Equal(1.25f, reloadedScene.Camera.Zoom);
            Assert.Equal(100f, reloadedScene.Camera.Position[0]);
            Assert.NotEmpty(reloadedScene.Layers);
        });
    }

    // ---- MembershipFilterTest: only tagged roots + ChildOf closure serialize; transient/untagged excluded ----

    [Fact]
    public void MembershipFilterTest()
    {
        using var world = new World();

        // Tagged root with a two-level child closure.
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Player", "Hero"));
        root.Set(new TransformComponent(Vector2.Zero));

        var child = world.CreateEntity();
        child.Set(new EntityInfoComponent("Orb", "BlueOrb"));
        child.Set(new TransformComponent(Vector2.Zero));
        child.SetParent(root);

        var grandchild = world.CreateEntity();
        grandchild.Set(new EntityInfoComponent("Orb", "RedOrb"));
        grandchild.Set(new TransformComponent(Vector2.Zero));
        grandchild.SetParent(child);

        // Transient / overlay entities — untagged → excluded.
        var cursor = world.CreateEntity();
        cursor.Set(new EntityInfoComponent("Cursor"));
        var uiWidget = world.CreateEntity();
        uiWidget.Set(new EntityInfoComponent("ToolbarButton"));
        var gizmo = world.CreateEntity();
        gizmo.Set(new EntityInfoComponent("GizmoHandle"));

        // Blender-style entity — untagged → excluded (GAP-B: Blender save deferred).
        var blender = world.CreateEntity();
        blender.Set(new EntityInfoComponent("Mesh", "blender_floor"));
        blender.Set(new TransformComponent(Vector2.Zero));

        var members = SceneWriter.CollectMembership(world);

        Assert.Equal(3, members.Count); // root + child + grandchild
        Assert.Contains(root, members);
        Assert.Contains(child, members);
        Assert.Contains(grandchild, members);
        Assert.DoesNotContain(cursor, members);
        Assert.DoesNotContain(uiWidget, members);
        Assert.DoesNotContain(gizmo, members);
        Assert.DoesNotContain(blender, members);
    }

    // ---- DerivedDepthReproductionTest: after reload, a prep+YSort frame recomputes the SAME derived depth ----

    [Fact]
    public void DerivedDepthReproductionTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            // A Y-sorted layer map: the "Characters" layer participates in Y-sort, so DrawComponent.LayerDepth
            // is derived per-frame from world Y (NOT the persisted SOURCE LayerDepth).
            var layers = DrawLayerMap.FromEnum<TestLayer>().WithYSort(TestLayer.Characters);
            var charactersDepth = layers.GetDepth(TestLayer.Characters);

            var camera = new MonoDreams.Component.Camera();
            camera.Position = new Vector2(0, 0);

            // ---- pre-save world: place a Y-sorted sprite, run one prep+YSort frame, capture derived depth ----
            using var preWorld = new World();
            var preDerived = BuildPrepAndYSort(preWorld, layers, camera, charactersDepth,
                worldY: 120f, ySortOffset: 7f, ySortBias: 0.0005f, out var preRoot);

            // Save (membership: the one tagged root).
            var registry = NewEngineRegistry();
            var serializer = new SceneSerializer(registry);
            new SceneWriter(serializer).Save(preWorld, SceneFileName, camera, layers);

            // ---- reload world: deserialize, then run the SAME prep+YSort frame ----
            using var postWorld = new World();
            var loadRegistry = NewEngineRegistry();
            var loadSerializer = new SceneSerializer(loadRegistry);
            var spy = new TextureLoadSpy();
            using var reader = new SceneReaderSystem(postWorld, loadSerializer, content: null, loadTexture: spy.Load);
            postWorld.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            var loadedRoot = CollectEntitiesWith<SpriteInfoComponent>(postWorld).Single();

            // The persisted SOURCE LayerDepth survived (the Y-sorted band depth), not a baked derived value.
            Assert.Equal(charactersDepth, loadedRoot.Get<SpriteInfoComponent>().LayerDepth);

            // Re-prep: DrawComponent.LayerDepth := SOURCE LayerDepth (what SpritePrepSystem does), add Visible,
            // then run YSortSystem — it must recompute the identical derived depth.
            loadedRoot.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
            loadedRoot.Set(new VisibleComponent());
            ref var draw = ref loadedRoot.Get<DrawComponent>();
            draw.LayerDepth = loadedRoot.Get<SpriteInfoComponent>().LayerDepth;

            using var ySort = new MonoDreams.System.Draw.YSortSystem(postWorld, camera, layers);
            ySort.Update(new GameState(new GameTime()));

            var postDerived = loadedRoot.Get<DrawComponent>().LayerDepth;

            Assert.Equal(preDerived, postDerived);
            // And it is genuinely the Y-derived value, not the raw source band depth (guards "SOURCE not derived").
            Assert.NotEqual(charactersDepth, postDerived);
        });
    }

    /// <summary>
    /// Places a sprite at <paramref name="worldY"/> on a Y-sorted layer, runs the derived-depth
    /// computation (DrawComponent.LayerDepth := SOURCE, then YSortSystem), and returns the derived
    /// depth. Mirrors what SpritePrepSystem (LayerDepth := SpriteInfo.LayerDepth) + YSortSystem do,
    /// without a GraphicsDevice.
    /// </summary>
    private static float BuildPrepAndYSort(World world, DrawLayerMap layers, MonoDreams.Component.Camera camera,
        float sourceLayerDepth, float worldY, float ySortOffset, float ySortBias, out Entity root)
    {
        root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Player", "Hero"));
        root.Set(new TransformComponent(new Vector2(0, worldY)));
        root.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Player",
            Size = new Vector2(16, 32),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = sourceLayerDepth,
            YSortOffset = ySortOffset,
            YSortDepthBias = ySortBias,
        });
        root.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        root.Set(new VisibleComponent());

        ref var draw = ref root.Get<DrawComponent>();
        draw.LayerDepth = sourceLayerDepth; // SpritePrepSystem's effect

        using var ySort = new MonoDreams.System.Draw.YSortSystem(world, camera, layers);
        ySort.Update(new GameState(new GameTime()));

        return root.Get<DrawComponent>().LayerDepth;
    }

    /// <summary>A minimal draw-layer enum for the round-trip + Y-sort tests (front → back).</summary>
    private enum TestLayer
    {
        Foreground,
        Characters,
        Background,
    }

    // ---- file: AssetKey round-trip (island-authoring Slice 1): place → save → reload ----

    /// <summary>
    /// A placed sprite prop's <c>file:</c> AssetKey (+ its sliced-region Source rect and the
    /// feet-origin SOURCE fields) round-trips through save → reload, and rehydration routes the
    /// <c>file:</c> key through the FILE-asset loader — never the content loader.
    /// </summary>
    [Fact]
    public void FileAssetKeyRoundTripTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var sourceWorld = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());

            // "Place" a prop exactly like the palette does: the generic factory + the save-root tag.
            var entry = new MonoDreams.LevelEditor.Assets.AssetCatalogEntry(
                "Island/props/sheet.png", "trunk", new Rectangle(0, 0, 32, 48), "sheet#trunk", "props");
            var band = new MonoDreams.LevelEditor.Assets.PaletteBand("Props", 0.45f, YSorted: true);
            var placed = MonoDreams.LevelEditor.Assets.SpritePropFactory.Create(
                sourceWorld, entry, band, new Vector2(120, 80), texture: null);
            placed.Set(new SceneObjectComponent()); // what CreateEntityCommand does on placement

            new SceneWriter(serializer).Save(sourceWorld, SceneFileName, camera: null, layers: null);

            // Reload onto a fresh world: file: keys must hit the file loader, not the content loader.
            using var loadWorld = new World();
            var contentSpy = new TextureLoadSpy();
            var fileKeys = new List<string>();
            using var reader = new SceneReaderSystem(loadWorld, new SceneSerializer(NewEngineRegistry()),
                content: null,
                loadTexture: contentSpy.Load,
                fileTextureLoader: key => { fileKeys.Add(key); return null; });

            loadWorld.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            var loaded = CollectEntitiesWith<SpriteInfoComponent>(loadWorld).Single();
            var sprite = loaded.Get<SpriteInfoComponent>();
            Assert.Equal("file:Island/props/sheet.png#trunk", sprite.AssetKey);
            Assert.Equal(new Rectangle(0, 0, 32, 48), sprite.Source); // the region rect round-trips
            Assert.Equal(new Vector2(16f, 48f), sprite.Origin); // feet-origin SOURCE field
            Assert.Equal(0.45f, sprite.LayerDepth);
            Assert.Equal(new Vector2(120, 80), loaded.Get<TransformComponent>().Position);

            Assert.Equal(new[] { "file:Island/props/sheet.png#trunk" }, fileKeys);
            Assert.Empty(contentSpy.RequestedKeys); // the content loader never sees a file: key
        });
    }

    /// <summary>
    /// A scene referencing a <c>file:</c> asset this checkout does not have loads with the shared
    /// magenta placeholder (through the REAL <c>FileAssetTextureLoader</c> missing-file path) —
    /// the failed path is recorded loudly, never a silently invisible entity.
    /// </summary>
    [Fact]
    public void MissingFileAssetOnReloadTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var sourceWorld = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());

            var entry = new MonoDreams.LevelEditor.Assets.AssetCatalogEntry(
                "Island/props/tree01.png", null, null, "tree01", "props");
            var band = new MonoDreams.LevelEditor.Assets.PaletteBand("Ground", 0.9f, YSorted: false);
            var placed = MonoDreams.LevelEditor.Assets.SpritePropFactory.Create(
                sourceWorld, entry, band, Vector2.Zero, texture: null);
            placed.Set(new SceneObjectComponent());

            new SceneWriter(serializer).Save(sourceWorld, SceneFileName, camera: null, layers: null);

            // Reload with the REAL file loader whose file is missing (openStream → null).
            var placeholderRequests = 0;
            var loader = new MonoDreams.LevelEditor.Assets.FileAssetTextureLoader(
                openStream: _ => null,
                decode: _ => null,
                createPlaceholder: () => { placeholderRequests++; return null; });

            using var loadWorld = new World();
            using var reader = new SceneReaderSystem(loadWorld, new SceneSerializer(NewEngineRegistry()),
                content: null,
                loadTexture: new TextureLoadSpy().Load,
                fileTextureLoader: loader.Load);

            loadWorld.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // The entity loaded; the loader took the missing-file path: recorded + placeholder
            // requested (in the real composition that placeholder is the visible magenta texture).
            var loaded = CollectEntitiesWith<SpriteInfoComponent>(loadWorld).Single();
            Assert.Equal("file:Island/props/tree01.png", loaded.Get<SpriteInfoComponent>().AssetKey);
            Assert.Equal(new[] { "Island/props/tree01.png" }, loader.MissingPaths);
            Assert.Equal(1, placeholderRequests);
        });
    }
}
