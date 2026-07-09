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

        // The camera RIG (UX2-E, pre-mortem #4): a standalone editor-materialized entity carrying the
        // authored camera state. It is NEVER SceneObjectComponent-tagged, so it must never enter
        // entities[] (the writer reads scene.camera FROM it explicitly, it is not scene membership).
        var cameraRig = world.CreateEntity();
        cameraRig.Set(new EditorInfrastructureComponent());
        cameraRig.Set(new TransformComponent(new Vector2(300, -200)));
        cameraRig.Set(new CameraRigComponent(2f, 0f));

        var members = SceneWriter.CollectMembership(world);

        Assert.Equal(3, members.Count); // root + child + grandchild
        Assert.Contains(root, members);
        Assert.Contains(child, members);
        Assert.Contains(grandchild, members);
        Assert.DoesNotContain(cursor, members);
        Assert.DoesNotContain(uiWidget, members);
        Assert.DoesNotContain(gizmo, members);
        Assert.DoesNotContain(blender, members);
        Assert.DoesNotContain(cameraRig, members); // the rig is never scene membership (pre-mortem #4)
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

    /// <summary>
    /// Island-authoring Slice 2 (§4.2): a within-band ordering nudge — applied through the REAL
    /// <c>EditorCommandSystem</c> actions — persists through save → load, because it only ever
    /// touches the serialized SOURCE sort fields (<c>LayerDepth</c> on a plain band,
    /// <c>YSortDepthBias</c> on a Y-sorted band).
    /// </summary>
    [Fact]
    public void OrderingPersistsThroughSaveLoadTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var layers = DrawLayerMap.FromEnum<TestLayer>().WithYSort(TestLayer.Characters);
            var groundDepth = layers.GetDepth(TestLayer.Background);
            var propsDepth = layers.GetDepth(TestLayer.Characters);

            using var world = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            var history = new MonoDreams.LevelEditor.Undo.EditorHistory(world);
            using var commands = new MonoDreams.LevelEditor.System.EditorCommandSystem(
                world, history, serializer,
                layers: layers);
            var edit = new GameState(new GameTime()) { RunMode = RunMode.Edit };

            Entity MakeProp(float depth)
            {
                var prop = world.CreateEntity();
                prop.Set(new SceneObjectComponent());
                prop.Set(new EntityInfoComponent("Prop", depth.ToString()));
                prop.Set(new TransformComponent(new Vector2(10, 10)));
                prop.Set(new SpriteInfoComponent
                {
                    Size = new Vector2(16, 16),
                    Target = RenderTargetID.Main,
                    LayerDepth = depth,
                });
                return prop;
            }

            // A ground patch nudged forward twice (LayerDepth moves)…
            var patch = MakeProp(groundDepth);
            patch.Set(new MonoDreams.LevelEditor.Component.SelectedComponent());
            commands.BringForward(edit);
            commands.BringForward(edit);
            patch.Remove<MonoDreams.LevelEditor.Component.SelectedComponent>();

            // …and a Y-sorted prop nudged back once (the BIAS moves, LayerDepth must not).
            var prop = MakeProp(propsDepth);
            prop.Set(new MonoDreams.LevelEditor.Component.SelectedComponent());
            commands.SendBack(edit);

            var step = MonoDreams.LevelEditor.System.EditorCommandSystem.OrderStep;
            Assert.Equal(groundDepth + 2 * step, patch.Get<SpriteInfoComponent>().LayerDepth, 6);
            Assert.Equal(propsDepth, prop.Get<SpriteInfoComponent>().LayerDepth);
            Assert.Equal(-step, prop.Get<SpriteInfoComponent>().YSortDepthBias, 6);

            new SceneWriter(serializer).Save(world, SceneFileName, camera: null, layers: layers);

            using var loadWorld = new World();
            using var reader = new SceneReaderSystem(loadWorld, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: new TextureLoadSpy().Load);
            loadWorld.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            var loadedPatch = CollectEntitiesWith<SpriteInfoComponent>(loadWorld)
                .Single(e => e.Get<SpriteInfoComponent>().LayerDepth != propsDepth);
            var loadedProp = CollectEntitiesWith<SpriteInfoComponent>(loadWorld)
                .Single(e => e.Get<SpriteInfoComponent>().LayerDepth == propsDepth);

            // The nudges round-trip exactly, still inside their bands.
            Assert.Equal(patch.Get<SpriteInfoComponent>().LayerDepth,
                loadedPatch.Get<SpriteInfoComponent>().LayerDepth);
            Assert.True(layers.TryGetBandRange(loadedPatch.Get<SpriteInfoComponent>().LayerDepth,
                out var band, out _, out _, out _));
            Assert.Equal(groundDepth, band);

            Assert.Equal(propsDepth, loadedProp.Get<SpriteInfoComponent>().LayerDepth);
            Assert.Equal(-step, loadedProp.Get<SpriteInfoComponent>().YSortDepthBias, 6);
            // The Y-sorted band membership survived (the exact-match lookup still hits).
            Assert.True(layers.TryGetYSortRange(loadedProp.Get<SpriteInfoComponent>().LayerDepth, out _, out _));
        });
    }

    /// <summary>
    /// Island-authoring Slice 3 (§5.2): a boundary's polyline serializes into <c>entities[]</c> but
    /// its baked segment colliders are NEVER written (the "bake products never scene-serialize"
    /// invariant) — on reload they regenerate from the polyline (bake-on-load, in Play). The
    /// polyline round-trips exactly.
    /// </summary>
    [Fact]
    public void BoundaryBakeChildrenNeverSerialize_RegenerateOnLoadTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            using var bake = new MonoDreams.LevelEditor.System.BoundaryBakeSystem(world);
            var edit = new GameState(new GameTime()) { RunMode = RunMode.Edit };

            // A committed boundary (a save-root) with a 3-point polyline (→ 2 baked segments).
            var localPoints = new[] { new Vector2(-40, 0), new Vector2(0, 0), new Vector2(40, 20) };
            var boundary = world.CreateEntity();
            boundary.Set(new SceneObjectComponent());
            boundary.Set(new EntityInfoComponent("Boundary", "boundary_01"));
            boundary.Set(new TransformComponent(new Vector2(200, 300)));
            boundary.Set(new MonoDreams.LevelEditor.Component.BoundaryComponent(localPoints, 24f));
            bake.Update(edit);

            // Baked children exist in the live world, but they are NOT tagged save-roots.
            using (var bakedSet = world.GetEntities()
                       .With<MonoDreams.LevelEditor.Component.BakedProductComponent>().AsSet())
            {
                var count = 0; foreach (var _ in bakedSet.GetEntities()) count++;
                Assert.Equal(2, count);
            }

            new SceneWriter(serializer).Save(world, SceneFileName, camera: null, layers: null);
            var saved = global::System.Text.Json.JsonSerializer.Deserialize<SceneData>(fake.Files[SceneFileName]);
            // The boundary root is written (one entity, carrying the boundary component); NO baked
            // convex-collider child appears.
            Assert.Single(saved.Entities);
            Assert.True(saved.Entities[0].Components.ContainsKey(
                MonoDreams.LevelEditor.Serialization.EngineComponentSerializers.BoundaryKey));
            Assert.DoesNotContain(saved.Entities, e => e.Components.ContainsKey(
                MonoDreams.LevelEditor.Serialization.EngineComponentSerializers.ConvexColliderKey));

            // Reload onto a fresh world with the bake system live → the segments regenerate.
            using var loadWorld = new World();
            using var loadBake = new MonoDreams.LevelEditor.System.BoundaryBakeSystem(loadWorld);
            using var reader = new SceneReaderSystem(loadWorld, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: new TextureLoadSpy().Load);
            var play = new GameState(new GameTime()) { RunMode = RunMode.Play };
            loadWorld.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));
            loadBake.Update(play); // bake-on-load runs in Play too

            using (var reloadedBaked = loadWorld.GetEntities()
                       .With<MonoDreams.LevelEditor.Component.BakedProductComponent>().AsSet())
            {
                var count = 0; foreach (var _ in reloadedBaked.GetEntities()) count++;
                Assert.Equal(2, count); // children regenerated
            }

            // The polyline round-tripped exactly.
            var reloaded = CollectEntitiesWith<MonoDreams.LevelEditor.Component.BoundaryComponent>(loadWorld).Single();
            var reloadedPoints = reloaded.Get<MonoDreams.LevelEditor.Component.BoundaryComponent>().Points;
            Assert.Equal(localPoints, reloadedPoints);
            Assert.Equal(24f, reloaded.Get<MonoDreams.LevelEditor.Component.BoundaryComponent>().Thickness);
            Assert.Equal(new Vector2(200, 300), reloaded.Get<TransformComponent>().Position);
        });
    }

    // ---- Bug 1 (Slice 3.5): a reloaded scene re-saves identically — load → edit → save is a fixed point ----

    /// <summary>
    /// The core iterate-on-a-level loop. <see cref="SceneObjectComponent"/> is transient editor state
    /// (never serialized), so a freshly reloaded scene carries no save-root tags; the reader must
    /// <b>re-tag each reconstructed scene root</b> or the next Save (which only writes tagged roots +
    /// their closure) writes an empty scene and silently loses every edit made since loading.
    ///
    /// <para>Mixed content (a placed prop, a boundary with baked segment children, a trigger) → save →
    /// reload via <see cref="LoadSceneRequest"/> → edit a loaded entity's transform → save again. The
    /// second save must equal the first (same 3 authored roots, boundary bake child still excluded)
    /// PLUS the edit. Before the fix the second save is EMPTY (0 entities) — this test fails.</para>
    /// </summary>
    [Fact]
    public void ReloadedSceneReTagsRoots_LoadEditSaveIsAFixedPoint()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var edit = new GameState(new GameTime()) { RunMode = RunMode.Edit };

            // ---- BUILD + SAVE #1 ----
            using var world1 = new World();
            using var bake1 = new MonoDreams.LevelEditor.System.BoundaryBakeSystem(world1);

            var prop = world1.CreateEntity();
            prop.Set(new SceneObjectComponent());
            prop.Set(new EntityInfoComponent("Prop", "tree"));
            prop.Set(new TransformComponent(new Vector2(120, 80)));
            prop.Set(new SpriteInfoComponent
            {
                AssetKey = "Atlas/tree", Size = new Vector2(16, 32),
                Target = RenderTargetID.Main, LayerDepth = 0.5f,
            });

            var trigger = MonoDreams.LevelEditor.Assets.TriggerFactory.Create(world1,
                new MonoDreams.LevelEditor.Assets.TriggerType("evidence", "Evidence", new Vector2(48, 48)),
                new Vector2(300, 50), "evidence_01");
            trigger.Set(new SceneObjectComponent());

            var boundary = world1.CreateEntity();
            boundary.Set(new SceneObjectComponent());
            boundary.Set(new EntityInfoComponent("Boundary", "boundary_01"));
            boundary.Set(new TransformComponent(new Vector2(200, 300)));
            boundary.Set(new MonoDreams.LevelEditor.Component.BoundaryComponent(
                new[] { new Vector2(-40, 0), new Vector2(0, 0), new Vector2(40, 20) }, 24f));
            bake1.Update(edit); // 2 baked segment children (never serialized)

            var save1Serializer = new SceneSerializer(NewEngineRegistry());
            new SceneWriter(save1Serializer).Save(world1, SceneFileName, camera: null, layers: null);
            var save1 = global::System.Text.Json.JsonSerializer.Deserialize<SceneData>(fake.Files[SceneFileName])!;
            Assert.Equal(3, save1.Entities.Count); // prop + trigger + boundary
            Assert.DoesNotContain(save1.Entities, e => e.Components.ContainsKey(EngineComponentSerializers.ConvexColliderKey));

            // ---- RELOAD onto a fresh world ----
            using var world2 = new World();
            using var bake2 = new MonoDreams.LevelEditor.System.BoundaryBakeSystem(world2);
            using var reader = new SceneReaderSystem(world2, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: new TextureLoadSpy().Load);
            world2.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));
            bake2.Update(edit); // regenerate the baked children

            // The reader re-tagged the 3 authored roots (without the fix this is 0 → save #2 empty).
            Assert.Equal(3, CollectEntitiesWith<SceneObjectComponent>(world2).Count);
            // The baked children exist but are NOT tagged (bake products never serialize).
            var baked = CollectEntitiesWith<MonoDreams.LevelEditor.Component.BakedProductComponent>(world2);
            Assert.Equal(2, baked.Count);
            Assert.DoesNotContain(baked, e => e.Has<SceneObjectComponent>());

            // ---- EDIT a loaded entity's transform ----
            var loadedProp = CollectEntitiesWith<SpriteInfoComponent>(world2)
                .Single(e => e.Has<EntityInfoComponent>() && e.Get<EntityInfoComponent>().Type == "Prop");
            loadedProp.Set(new TransformComponent(new Vector2(999, 111)));

            // ---- SAVE #2 ----
            var save2Serializer = new SceneSerializer(NewEngineRegistry());
            new SceneWriter(save2Serializer).Save(world2, SceneFileName, camera: null, layers: null);
            var save2 = global::System.Text.Json.JsonSerializer.Deserialize<SceneData>(fake.Files[SceneFileName])!;

            // FIXED POINT: same root set (3, NOT 0), boundary bake child still excluded.
            Assert.Equal(save1.Entities.Count, save2.Entities.Count);
            Assert.Equal(3, save2.Entities.Count);
            Assert.DoesNotContain(save2.Entities, e => e.Components.ContainsKey(EngineComponentSerializers.ConvexColliderKey));
            Assert.Single(save2.Entities, e => e.Components.ContainsKey(EngineComponentSerializers.BoundaryKey));

            // The edit PERSISTED: the prop's serialized transform is the new position.
            var savedProp = save2.Entities.Single(e =>
                e.Components.TryGetValue(EngineComponentSerializers.EntityInfoKey, out var info)
                && info.GetProperty("type").GetString() == "Prop");
            var pos = savedProp.Components[EngineComponentSerializers.TransformKey]
                .GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Assert.Equal(999f, pos[0]);
            Assert.Equal(111f, pos[1]);
        });
    }
}
