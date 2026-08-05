using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Platform;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave 2 component-serializer registry (the in-game level editor's scene
/// persistence substrate). Pure logic — a <see cref="World"/> and hand-built entities, no
/// rendering and no live <c>GraphicsDevice</c>.
///
/// Covers the level-editor premises:
/// - "Scene round-trip reconstructs from registered components, not factories" (the registry
///   half: an entity's registered components + the structural parent link round-trip through the
///   in-memory <see cref="SceneData"/>).
/// - "The registry is opt-in; unregistered components are skipped with a loud warning, not thrown".
/// - "SpriteInfo serializes the AssetKey + SOURCE sort fields, never a live Texture2D".
///
/// Also covers the level-loading premise "Scene layers are entities; member draw order derives from
/// (layer order, within-layer key)" — the persistence half: <c>core.SceneLayer</c> round-trips the
/// four AUTHORED layer fields (and only those; the final depth is per-frame-derived). The draw-remap
/// half lives in <c>MonoDreams.Tests/Rendering/SceneLayerSystemTests.cs</c>.
///
/// And the level-loading premise "The paint grid is authored cells + values; everything
/// visible/collidable is a bake product" — the persistence half: <c>core.TileGrid</c> round-trips
/// the paint VALUES and the sparse CELLS canonically (cells sorted by (y, x), activeLayers sorted)
/// and nothing else; the derived tiles/colliders are bake products, tested in
/// <c>TileGridBakeSystemTests</c> and <c>TileGridBakingTests</c>.
///
/// The warning assertion mutates the process-global <see cref="Logger"/> and
/// <see cref="PlatformServices.Current"/>, so that test is isolated in the non-parallel collection.
/// </summary>
public class ComponentSerializerRegistryTest
{
    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    // ---- Full round-trip: Transform + SpriteInfo(AssetKey) + BoxCollider + a ChildOf parent link ----

    [Fact]
    public void RoundTrip_ReproducesRegisteredComponents_AndParentLink()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        var serializer = new SceneSerializer(registry);

        // Parent: Transform + SpriteInfo (AssetKey set) + BoxCollider + EntityInfo + RigidBody/Velocity.
        var parent = world.CreateEntity();
        parent.Set(new EntityInfoComponent("Player", "Hero"));
        parent.Set(new TransformComponent(new Vector2(10, 20), rotation: 0.5f,
            scale: new Vector2(2, 3), origin: new Vector2(1, 1)));
        parent.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Player",
            Source = new Rectangle(4, 8, 16, 32),
            Size = new Vector2(16, 32),
            Color = new Color(10, 20, 30, 40),
            Origin = new Vector2(2, 2),
            Offset = new Vector2(3, 4),
            Target = RenderTargetID.Main,
            LayerDepth = 0.42f,
            YSortOffset = 7f,
            YSortDepthBias = 0.001f,
        });
        parent.Set(new BoxColliderComponent(new Vector2(16, 32), new HashSet<int> { 1, 2 }, passive: true, enabled: false));
        parent.Set(new RigidBodyComponent(mass: 5f, isKinematic: true, gravityActive: false, gravityScale: 0.25f));
        parent.Set(new VelocityComponent(new Vector2(1, -2)));

        // Child: Transform + ChildOf(parent) link.
        var child = world.CreateEntity();
        child.Set(new EntityInfoComponent("Orb"));
        child.Set(new TransformComponent(new Vector2(50, 0)));
        child.SetParent(parent);

        var scene = serializer.Serialize(new List<Entity> { parent, child });

        // The child's parent index points at the parent's index (0).
        Assert.Null(scene.Entities[0].Parent);
        Assert.Equal(0, scene.Entities[1].Parent);

        // Deserialize onto a FRESH world (no factories re-run).
        using var freshWorld = new World();
        var loaded = serializer.Deserialize(freshWorld, scene);
        Assert.Equal(2, loaded.Count);

        var loadedParent = loaded[0];
        var loadedChild = loaded[1];

        // Transform reproduces exactly.
        var t = loadedParent.Get<TransformComponent>();
        Assert.Equal(new Vector2(10, 20), t.Position);
        Assert.Equal(0.5f, t.Rotation);
        Assert.Equal(new Vector2(2, 3), t.Scale);
        Assert.Equal(new Vector2(1, 1), t.Origin);

        // SpriteInfo reproduces source data + the AssetKey, and carries NO live texture.
        var s = loadedParent.Get<SpriteInfoComponent>();
        Assert.Equal("Atlas/TX Player", s.AssetKey);
        Assert.Null(s.SpriteSheet); // rehydration from AssetKey is Wave 3's reader job
        Assert.Equal(new Rectangle(4, 8, 16, 32), s.Source);
        Assert.Equal(new Vector2(16, 32), s.Size);
        Assert.Equal(new Color(10, 20, 30, 40), s.Color);
        Assert.Equal(new Vector2(2, 2), s.Origin);
        Assert.Equal(new Vector2(3, 4), s.Offset);
        Assert.Equal(RenderTargetID.Main, s.Target);
        // SOURCE sort fields round-trip.
        Assert.Equal(0.42f, s.LayerDepth);
        Assert.Equal(7f, s.YSortOffset);
        Assert.Equal(0.001f, s.YSortDepthBias);

        // BoxCollider reproduces shape + layers + flags.
        var box = loadedParent.Get<BoxColliderComponent>();
        Assert.Equal(new Vector2(16, 32), box.Size);
        Assert.Equal(new HashSet<int> { 1, 2 }, box.ActiveLayers);
        Assert.True(box.Passive);
        Assert.False(box.Enabled);

        // RigidBody + Velocity reproduce.
        var rb = loadedParent.Get<RigidBodyComponent>();
        Assert.Equal(5f, rb.Mass);
        Assert.True(rb.IsKinematic);
        Assert.False(rb.Gravity.active);
        Assert.Equal(0.25f, rb.Gravity.factor);
        Assert.Equal(new Vector2(1, -2), loadedParent.Get<VelocityComponent>().Current);

        // EntityInfo reproduces.
        Assert.Equal("Player", loadedParent.Get<EntityInfoComponent>().Type);
        Assert.Equal("Hero", loadedParent.Get<EntityInfoComponent>().Name);

        // The parent link reproduces: ChildOfComponent points at the loaded parent, and the
        // TransformComponent matrix link is synced (SetParent wires both with both transforms present).
        Assert.True(loadedChild.Has<ChildOfComponent>());
        Assert.Equal(loadedParent, loadedChild.Get<ChildOfComponent>().Parent);
        Assert.Same(loadedParent.Get<TransformComponent>(), loadedChild.Get<TransformComponent>().Parent);
    }

    // ---- core.SceneLayer round-trips (the designer's layer entity: order/visible/locked/screenSpace) ----

    [Fact]
    public void SceneLayer_RoundTrips_AllFourAuthoredFields()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        var serializer = new SceneSerializer(registry);

        // A layer entity is nothing but its name + SceneLayerComponent — every field non-default,
        // so a dropped field cannot pass by coinciding with the default.
        var layer = world.CreateEntity();
        layer.Set(new EntityInfoComponent("Layer", "Background"));
        layer.Set(new SceneLayerComponent { Order = 3, Visible = false, Locked = true, ScreenSpace = true });

        var scene = serializer.Serialize(new List<Entity> { layer });

        var entry = scene.Entities[0];
        Assert.True(entry.Components.ContainsKey(EngineComponentSerializers.SceneLayerKey));
        var json = entry.Components[EngineComponentSerializers.SceneLayerKey];
        Assert.Equal(3, json.GetProperty("order").GetInt32());
        Assert.False(json.GetProperty("visible").GetBoolean());
        Assert.True(json.GetProperty("locked").GetBoolean());
        Assert.True(json.GetProperty("screenSpace").GetBoolean());

        // Deserialize onto a FRESH world: all four authored fields reproduce.
        using var freshWorld = new World();
        var loaded = serializer.Deserialize(freshWorld, scene);
        Assert.Single(loaded);

        var reloaded = loaded[0].Get<SceneLayerComponent>();
        Assert.Equal(3, reloaded.Order);
        Assert.False(reloaded.Visible);
        Assert.True(reloaded.Locked);
        Assert.True(reloaded.ScreenSpace);
        // The layer's NAME is its EntityInfo (no name field on the layer component).
        Assert.Equal("Background", loaded[0].Get<EntityInfoComponent>().Name);

        // Byte-stable: write → read → write produces identical JSON (no drift across a save cycle).
        var rewritten = registry.SerializeEntity(loaded[0]).Components[EngineComponentSerializers.SceneLayerKey];
        Assert.Equal(json.GetRawText(), rewritten.GetRawText());

        // The derived final draw depth is never persisted — only the authored layer fields are.
        Assert.DoesNotContain("layerDepth", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneLayer_IsInTheRegistryInventory()
    {
        var registry = NewEngineRegistry();

        // The registry inventory drives the Inspector's "+ Add component" candidates — a serializer
        // registered but missing from the inventory would be invisible to the editor.
        Assert.Equal(typeof(SceneLayerComponent), registry.TypeForKey(EngineComponentSerializers.SceneLayerKey));
        Assert.True(registry.IsRegistered(typeof(SceneLayerComponent)));
        Assert.False(registry.IsStructural(typeof(SceneLayerComponent))); // ordinary designer data
        Assert.Contains(registry.RegisteredComponents(),
            kv => kv.Key == EngineComponentSerializers.SceneLayerKey && kv.Type == typeof(SceneLayerComponent));
    }

    // ---- core.TileGrid round-trips the AUTHORED grid (values + sparse cells), canonically ----

    private static int[] Triple(JsonElement cell) =>
        new[] { cell[0].GetInt32(), cell[1].GetInt32(), cell[2].GetInt32() };

    [Fact]
    public void TileGrid_RoundTrips_ValuesAndCells_Canonically()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        var serializer = new SceneSerializer(registry);

        var grid = new TileGridComponent { CellSize = 24f };
        // Value 1 — every optional field set, and ActiveLayers deliberately UNSORTED on the way in.
        grid.Values.Add(new TilePaintValue
        {
            Id = 1,
            Name = "Wall",
            Color = new Color(10, 20, 30, 40),
            ActiveLayers = new[] { 7, 2, 5 },
            Passive = false,
            EntityType = "Blocker",
            TilesetKey = "Atlas/Tiles",
            TileSize = 16,
            AutotileRules = "15:1,1|6,0 6:0,0",
            LayerDepth = 0.75f,
        });
        // Value 2 — the visual-less, collision-less shape: no layers, no tileset, no entity type.
        grid.Values.Add(new TilePaintValue
        {
            Id = 2,
            Name = "Decor",
            ActiveLayers = Array.Empty<int>(),
            Passive = true,
            TileSize = 8,
            LayerDepth = 0.1f,
        });
        // Cells inserted OUT of canonical order, and including negative coordinates (the grid
        // entity's transform is the anchor, so painting up/left of it is ordinary).
        grid.Cells[TileGridComponent.Pack(3, 1)] = 1;
        grid.Cells[TileGridComponent.Pack(-2, -1)] = 2;
        grid.Cells[TileGridComponent.Pack(0, 1)] = 1;
        grid.Cells[TileGridComponent.Pack(-2, 4)] = 1;

        var e = world.CreateEntity();
        e.Set(new EntityInfoComponent("Terrain", "Paint"));
        e.Set(grid);

        var scene = serializer.Serialize(new List<Entity> { e });
        var json = scene.Entities[0].Components[EngineComponentSerializers.TileGridKey];

        // The payload is the AUTHORED data and nothing else — no derived tiles, no collider rects.
        var properties = json.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(3, properties.Length);
        Assert.Contains("cellSize", properties);
        Assert.Contains("values", properties);
        Assert.Contains("cells", properties);

        // Canonical bytes: cells are [x, y, value] triples sorted by (y, x)...
        var cells = json.GetProperty("cells");
        Assert.Equal(4, cells.GetArrayLength());
        Assert.Equal(new[] { -2, -1, 2 }, Triple(cells[0]));
        Assert.Equal(new[] { 0, 1, 1 }, Triple(cells[1]));
        Assert.Equal(new[] { 3, 1, 1 }, Triple(cells[2]));
        Assert.Equal(new[] { -2, 4, 1 }, Triple(cells[3]));
        // ...and activeLayers are sorted (an unsorted write makes two identical grids diff).
        var layers = json.GetProperty("values")[0].GetProperty("activeLayers");
        Assert.Equal(new[] { 2, 5, 7 },
            new[] { layers[0].GetInt32(), layers[1].GetInt32(), layers[2].GetInt32() });

        // Deserialize onto a FRESH world: every authored field reproduces.
        using var freshWorld = new World();
        var loaded = serializer.Deserialize(freshWorld, scene);
        var loadedEntity = Assert.Single(loaded);
        var reloaded = loadedEntity.Get<TileGridComponent>();

        Assert.Equal(24f, reloaded.CellSize);
        Assert.Equal(2, reloaded.Values.Count);

        var wall = reloaded.Values[0];
        Assert.Equal((byte)1, wall.Id);
        Assert.Equal("Wall", wall.Name);
        Assert.Equal(new Color(10, 20, 30, 40), wall.Color);
        Assert.Equal(new[] { 2, 5, 7 }, wall.ActiveLayers); // sorted on write, preserved on read
        Assert.False(wall.Passive);
        Assert.Equal("Blocker", wall.EntityType);
        Assert.Equal("Atlas/Tiles", wall.TilesetKey);
        Assert.Equal(16, wall.TileSize);
        Assert.Equal("15:1,1|6,0 6:0,0", wall.AutotileRules);
        Assert.Equal(0.75f, wall.LayerDepth);

        var decor = reloaded.Values[1];
        Assert.Equal((byte)2, decor.Id);
        Assert.Equal("Decor", decor.Name);
        Assert.Empty(decor.ActiveLayers); // empty layers = a paint that bakes no colliders
        Assert.True(decor.Passive);
        Assert.Null(decor.EntityType);
        Assert.Null(decor.TilesetKey); // null tileset = a paint that bakes no visuals
        Assert.Equal(8, decor.TileSize);
        Assert.Equal(0.1f, decor.LayerDepth);

        Assert.Equal(4, reloaded.Cells.Count);
        Assert.Equal((byte)1, reloaded.Cells[TileGridComponent.Pack(3, 1)]);
        Assert.Equal((byte)2, reloaded.Cells[TileGridComponent.Pack(-2, -1)]);
        Assert.Equal((byte)1, reloaded.Cells[TileGridComponent.Pack(0, 1)]);
        Assert.Equal((byte)1, reloaded.Cells[TileGridComponent.Pack(-2, 4)]);

        // Byte-stable: write → read → write produces identical JSON (a level's git diff stays
        // meaningful, and `load → save` is a fixed point).
        var rewritten = registry.SerializeEntity(loadedEntity).Components[EngineComponentSerializers.TileGridKey];
        Assert.Equal(json.GetRawText(), rewritten.GetRawText());
    }

    [Fact]
    public void TileGrid_IsInTheRegistryInventory()
    {
        var registry = NewEngineRegistry();

        // The inventory drives the Inspector's "+ Add component" candidates — a paint grid that
        // serializes but is invisible to the editor could never be authored.
        Assert.Equal(typeof(TileGridComponent), registry.TypeForKey(EngineComponentSerializers.TileGridKey));
        Assert.True(registry.IsRegistered(typeof(TileGridComponent)));
        Assert.False(registry.IsStructural(typeof(TileGridComponent))); // ordinary designer data
        Assert.Contains(registry.RegisteredComponents(),
            kv => kv.Key == EngineComponentSerializers.TileGridKey && kv.Type == typeof(TileGridComponent));
    }

    // ---- SpriteInfo serialization never references a live Texture2D (asset-key only) ----

    [Fact]
    public void SpriteInfo_Serialization_CarriesAssetKey_AndSourceSortFields_NeverLiveTexture()
    {
        using var world = new World();
        var registry = NewEngineRegistry();

        var e = world.CreateEntity();
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/Tiles",
            LayerDepth = 0.3f,
            YSortOffset = 5f,
            YSortDepthBias = 0.002f,
            Color = new Color(1, 2, 3, 4),
        });

        var data = registry.SerializeEntity(e);
        var json = data.Components[EngineComponentSerializers.SpriteInfoKey];

        // The asset key is present...
        Assert.Equal("Atlas/Tiles", json.GetProperty("assetKey").GetString());
        // ...the SOURCE sort fields are present...
        Assert.Equal(0.3f, json.GetProperty("layerDepth").GetSingle());
        Assert.Equal(5f, json.GetProperty("ySortOffset").GetSingle());
        Assert.Equal(0.002f, json.GetProperty("ySortDepthBias").GetSingle());
        // ...and no serialized field references a live Texture2D / SpriteSheet (it is not persisted).
        var raw = json.GetRawText();
        Assert.DoesNotContain("SpriteSheet", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Texture", raw, StringComparison.OrdinalIgnoreCase);
        foreach (var prop in json.EnumerateObject())
            Assert.NotEqual(JsonValueKind.Object, prop.Value.ValueKind); // no nested texture-like object
    }

    // ---- Opt-in: only registered types serialize; engine tags are never written ----

    [Fact]
    public void Registry_OptIn_SkipsUnregisteredEngineTags()
    {
        using var world = new World();
        var registry = NewEngineRegistry();

        var e = world.CreateEntity();
        e.Set(new TransformComponent(new Vector2(1, 1)));
        e.Set(new VisibleComponent());     // engine tag — deliberately NOT registered
        e.Set(new ColliderTagComponent()); // engine tag — deliberately NOT registered

        var data = registry.SerializeEntity(e);

        Assert.True(data.Components.ContainsKey(EngineComponentSerializers.TransformKey));
        Assert.Single(data.Components); // only the Transform was written; tags skipped
    }

    // ---- Unregistered component is skipped with a loud warning, NOT thrown ----

    /// <summary>A game component the engine registry does not know about.</summary>
    private struct UnregisteredGameComponent
    {
        public int Value;
    }

    [Collection("PlatformServices (non-parallel: mutates static state)")]
    public class UnregisteredComponentWarning
    {
        private sealed class CapturingPlatformServices : IPlatformServices
        {
            public StringWriter LogWriter { get; } = new();
            public List<string> ConsoleLines { get; } = new();
            public string BaseDirectory => "/fake/";
            public string GetEnvironmentVariable(string name) => null!;
            public string CombinePath(params string[] paths) => string.Join("/", paths);
            public bool FileExists(string path) => false;
            public string ReadAllText(string path) => throw new FileNotFoundException(path);
            public void WriteAllText(string path, string contents) { }
            public void WriteAllBytes(string path, byte[] bytes) { }
            public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
            public void CreateDirectory(string path) { }
            public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
            public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
            public void RunBackground(Action work) => work();
        }

        [Fact]
        public void SerializeEntity_WithUnregisteredComponent_SkipsAndWarns_DoesNotThrow()
        {
            var previous = PlatformServices.Current;
            var fake = new CapturingPlatformServices();
            try
            {
                PlatformServices.Current = fake;
                Logger.Shutdown();
                Logger.Initialize("logdir");

                using var world = new World();
                var registry = new ComponentSerializerRegistry();
                registry.RegisterEngineComponents();

                var e = world.CreateEntity();
                e.Set(new TransformComponent(new Vector2(2, 2)));
                e.Set(new UnregisteredGameComponent { Value = 99 }); // no serializer registered

                // It must not throw — the unregistered component is skipped.
                var data = registry.SerializeEntity(e);

                // The registered Transform was written; the unknown component was dropped.
                Assert.True(data.Components.ContainsKey(EngineComponentSerializers.TransformKey));
                Assert.Single(data.Components);

                Logger.Shutdown(); // flush
                var log = fake.LogWriter.ToString();
                Assert.Contains("No serializer registered", log);
                Assert.Contains(nameof(UnregisteredGameComponent), log);
            }
            finally
            {
                Logger.Shutdown();
                PlatformServices.Current = previous;
            }
        }
    }

    // ---- The game-component extension seam: register a serializer for a game component ----

    private struct GameScore
    {
        public int Points;
    }

    [Fact]
    public void GameComponent_RegistersOwnSerializer_AndRoundTrips()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        registry.Register(
            "game.Score",
            typeof(GameScore),
            write: e => JsonSerializer.SerializeToElement(new { points = e.Get<GameScore>().Points }),
            read: (e, json) => e.Set(new GameScore { Points = json.GetProperty("points").GetInt32() }));
        var serializer = new SceneSerializer(registry);

        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(new GameScore { Points = 1234 });

        var scene = serializer.Serialize(new List<Entity> { e });

        using var freshWorld = new World();
        var loaded = serializer.Deserialize(freshWorld, scene);

        Assert.Equal(1234, loaded[0].Get<GameScore>().Points);
    }

    // ---- A scene file referencing an unregistered component key fails LOUD on load ----

    [Fact]
    public void Deserialize_WithUnregisteredKey_ThrowsLoud()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        var serializer = new SceneSerializer(registry);

        var scene = new SceneData();
        var entityData = new SceneEntityData();
        entityData.Components["game.Unknown"] = JsonSerializer.SerializeToElement(new { x = 1 });
        scene.Entities.Add(entityData);

        var ex = Assert.Throws<InvalidOperationException>(() => serializer.Deserialize(world, scene));
        Assert.Contains("game.Unknown", ex.Message);
    }

    // ---- Registering the same type or key twice is rejected ----

    [Fact]
    public void Register_Duplicate_Throws()
    {
        var registry = NewEngineRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterEngineComponents()); // re-registering engine types
    }
}
