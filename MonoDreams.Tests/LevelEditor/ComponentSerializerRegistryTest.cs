using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
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
