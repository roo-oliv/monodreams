#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Serialization;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects PS5 <b>LDtk/Blender import-only + Examples migration</b>: a world shaped like the LDtk
/// factories / Blender parser output (player + orb sub-graph + tiles + walls; NPCs + colliders +
/// stop-motion) is tagged and serialized by <see cref="LevelImporter"/> and reloaded through the real
/// native reader (<see cref="SceneReaderSystem"/>) into an <b>equivalent</b> world — same entity set,
/// game components included, transforms + parent graph preserved. Reconstruction is by components, never
/// by re-running the parser.
///
/// Pure logic — hand-built entities mirroring the factory/parser output (the sanctioned testable core:
/// no <c>GraphicsDevice</c>, no real disk), an in-memory platform for the reader's file read, and a
/// null texture stub (AssetKey is preserved; only the live <c>Texture2D</c> is skipped).
///
/// Covers the level-editor premise "LDtk/Blender are import-only; the importer round-trips to native".
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class LevelImporterTests
{
    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/import/";
        public string GetEnvironmentVariable(string name) => null!;
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

    private static ComponentSerializerRegistry FullRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        r.RegisterGameComponents();
        return r;
    }

    private static List<Entity> With<T>(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<T>().AsSet();
        list.AddRange(set.GetEntities().ToArray());
        return list;
    }

    /// <summary>Import <paramref name="source"/> to native JSON, reload it through the real
    /// <see cref="SceneReaderSystem"/> into a fresh world, and hand the fresh world to
    /// <paramref name="assert"/>. The importer + reader share one full registry.</summary>
    private static void ImportReload(World source, Action<World> assert)
    {
        var registry = FullRegistry();
        var serializer = new SceneSerializer(registry);
        var importer = new LevelImporter(new SceneWriter(serializer));
        var json = importer.ImportToJson(source);

        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            fake.WriteAllText("scene.mdscene", json);
            using var dst = new World();
            // content:null + a null texture stub — AssetKey is preserved on the reloaded SpriteInfo;
            // only the live Texture2D rehydration is skipped (no GraphicsDevice in a unit test).
            var reader = new SceneReaderSystem(dst, serializer, content: null!, loadTexture: _ => (Texture2D)null!);
            dst.Publish(new LoadSceneRequest("scene.mdscene", fromContent: false));
            assert(dst);
        });
    }

    // ---- LDtk-like world (PlayerEntityFactory + Tile/Wall factories) ----

    private static void BuildLdtkLikeWorld(World w)
    {
        // Player + orb sub-graph, mirroring PlayerEntityFactory.
        var player = w.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        player.Set(new PlayerState());
        player.Set(new TransformComponent(new Vector2(96, 112)));
        player.Set(new BoxColliderComponent(new Vector2(16, 24), passive: false));
        player.Set(new RigidBodyComponent());
        player.Set(new VelocityComponent());
        player.Set(new CameraFollowTargetComponent { DampingX = 5f, DampingY = 5f, MaxDistanceX = 150f, MaxDistanceY = 100f });
        player.Set(new SpriteInfoComponent { AssetKey = "Atlas/TX Player", Source = new Rectangle(0, 0, 16, 24), Size = new Vector2(16, 24) });

        var blueOrb = w.CreateEntity();
        blueOrb.Set(new EntityInfoComponent("Orb", "BlueOrb"));
        blueOrb.Set(new TransformComponent(new Vector2(50, 0)));
        blueOrb.Set(new OrbitalMotion { Angle = 0f, Radius = 300f, Speed = 1f, CenterOffset = new Vector2(8, 16) });
        blueOrb.SetParent(player);

        var redOrb = w.CreateEntity();
        redOrb.Set(new EntityInfoComponent("Orb", "RedOrb"));
        redOrb.Set(new TransformComponent(new Vector2(20, 0)));
        redOrb.Set(new OrbitalMotion { Angle = 2.09f, Radius = 20f, Speed = 6f, CenterOffset = Vector2.Zero });
        redOrb.SetParent(blueOrb);

        // Two grass tiles + one wall tile, mirroring Tile/Wall factories (with AssetKey set by PS5).
        for (var i = 0; i < 2; i++)
        {
            var tile = w.CreateEntity();
            tile.Set(new EntityInfoComponent("Tile"));
            tile.Set(new TransformComponent(new Vector2(16 * i, 200)));
            tile.Set(new SpriteInfoComponent { AssetKey = "Atlas/TX Tileset Grass", Source = new Rectangle(0, 0, 16, 16), Size = new Vector2(16, 16) });
        }

        var wall = w.CreateEntity();
        wall.Set(new EntityInfoComponent("Wall"));
        wall.Set(new TransformComponent(new Vector2(0, 300)));
        wall.Set(new SpriteInfoComponent { AssetKey = "Atlas/TX Tileset Wall", Source = new Rectangle(0, 0, 16, 16), Size = new Vector2(16, 16) });
        wall.Set(new BoxColliderComponent(new Vector2(16, 16), passive: true));
        wall.Set(new RigidBodyComponent());
    }

    [Fact]
    public void LdtkLikeWorld_ImportReload_ReconstructsEquivalentWorld()
    {
        using var src = new World();
        BuildLdtkLikeWorld(src);

        ImportReload(src, dst =>
        {
            // Same entity set (6: player, blue orb, red orb, 2 tiles, wall).
            Assert.Equal(6, With<EntityInfoComponent>(dst).Count);

            var players = With<PlayerState>(dst);
            Assert.Single(players);
            var player = players[0];
            Assert.Equal(new Vector2(96, 112), player.Get<TransformComponent>().Position);
            Assert.True(player.Has<CameraFollowTargetComponent>());
            Assert.Equal(150f, player.Get<CameraFollowTargetComponent>().MaxDistanceX);
            Assert.Equal("Atlas/TX Player", player.Get<SpriteInfoComponent>().AssetKey);

            // Orb sub-graph: 2 OrbitalMotion entities, parent chain preserved.
            var orbs = With<OrbitalMotion>(dst);
            Assert.Equal(2, orbs.Count);
            var blue = orbs.Single(e => e.Get<EntityInfoComponent>().Name == "BlueOrb");
            var red = orbs.Single(e => e.Get<EntityInfoComponent>().Name == "RedOrb");
            Assert.True(blue.Has<ChildOfComponent>());
            Assert.True(blue.Get<ChildOfComponent>().Parent.Has<PlayerState>());   // blue → player
            Assert.True(red.Get<ChildOfComponent>().Parent.Has<OrbitalMotion>());  // red → blue orb
            Assert.Equal(300f, blue.Get<OrbitalMotion>().Radius);

            // Tiles + wall keep their tileset content keys (so the native reader can re-load them).
            var tiles = With<EntityInfoComponent>(dst).Where(e => e.Get<EntityInfoComponent>().Type == "Tile").ToList();
            Assert.Equal(2, tiles.Count);
            Assert.All(tiles, t => Assert.Equal("Atlas/TX Tileset Grass", t.Get<SpriteInfoComponent>().AssetKey));

            var wall = With<EntityInfoComponent>(dst).Single(e => e.Get<EntityInfoComponent>().Type == "Wall");
            Assert.Equal("Atlas/TX Tileset Wall", wall.Get<SpriteInfoComponent>().AssetKey);
            Assert.True(wall.Has<BoxColliderComponent>());
        });
    }

    // ---- Blender-like world (BlenderLevelParserSystem + game NPC/Player handlers) ----

    private static void BuildBlenderLikeWorld(World w)
    {
        var quad = new[] { new Vector2(-8, -18), new Vector2(8, -18), new Vector2(8, 18), new Vector2(-8, 18) };

        // Pete (Player).
        var pete = w.CreateEntity();
        pete.Set(new EntityInfoComponent("Player", "Pete"));
        pete.Set(new PlayerState());
        pete.Set(new TransformComponent(new Vector2(48, 20)));
        pete.Set(new SpriteInfoComponent { AssetKey = "GreasePencil/Pete", Source = new Rectangle(0, 0, 16, 36), Size = new Vector2(16, 36) });
        pete.Set(new StopMotionEffect { OffsetRadians = 0.035f });
        pete.Set(new ConvexColliderComponent(quad));
        pete.Set(new RigidBodyComponent());
        pete.Set(new VelocityComponent());
        pete.Set(new CameraFollowTargetComponent { MaxDistanceX = 150f });

        // Boldo (NPC).
        var boldo = w.CreateEntity();
        boldo.Set(new EntityInfoComponent("NPC", "Boldo"));
        boldo.Set(new TransformComponent(new Vector2(80, -33)));
        boldo.Set(new SpriteInfoComponent { AssetKey = "GreasePencil/Boldo", Source = new Rectangle(0, 0, 18, 35), Size = new Vector2(18, 35) });
        boldo.Set(new StopMotionEffect { OffsetRadians = 0.035f });
        boldo.Set(new ConvexColliderComponent(quad));

        // store (Collision).
        var store = w.CreateEntity();
        store.Set(new EntityInfoComponent("Collision", "store"));
        store.Set(new TransformComponent(new Vector2(120, -10)));
        store.Set(new SpriteInfoComponent { AssetKey = "GreasePencil/store", Source = new Rectangle(0, 0, 64, 48), Size = new Vector2(64, 48) });
        store.Set(new ConvexColliderComponent(quad));
    }

    [Fact]
    public void BlenderLikeWorld_ImportReload_ReconstructsEquivalentWorld()
    {
        using var src = new World();
        BuildBlenderLikeWorld(src);

        ImportReload(src, dst =>
        {
            Assert.Equal(3, With<EntityInfoComponent>(dst).Count);

            var pete = With<PlayerState>(dst).Single();
            Assert.Equal("Pete", pete.Get<EntityInfoComponent>().Name);
            Assert.Equal(new Vector2(48, 20), pete.Get<TransformComponent>().Position);
            Assert.True(pete.Has<StopMotionEffect>());
            Assert.True(pete.Has<CameraFollowTargetComponent>());
            Assert.True(pete.Has<ConvexColliderComponent>());
            Assert.Equal("GreasePencil/Pete", pete.Get<SpriteInfoComponent>().AssetKey);

            var npcs = With<EntityInfoComponent>(dst).Where(e => e.Get<EntityInfoComponent>().Type == "NPC").ToList();
            var boldo = Assert.Single(npcs);
            Assert.Equal("Boldo", boldo.Get<EntityInfoComponent>().Name);
            Assert.True(boldo.Has<StopMotionEffect>());
            Assert.True(boldo.Has<ConvexColliderComponent>());
            Assert.Equal(0.035f, boldo.Get<StopMotionEffect>().OffsetRadians);
        });
    }

    [Fact]
    public void TagContentRoots_TagsTopLevelContent_ExcludesInfraAndBake()
    {
        using var w = new World();

        var content = w.CreateEntity();
        content.Set(new EntityInfoComponent("Wall"));
        content.Set(new TransformComponent(Vector2.Zero));

        var child = w.CreateEntity();
        child.Set(new EntityInfoComponent("Child"));
        child.Set(new TransformComponent(Vector2.Zero));
        child.SetParent(content); // a ChildOf descendant — not a root, pulled in by the closure

        var infra = w.CreateEntity();
        infra.Set(new EditorInfrastructureComponent());
        infra.Set(new TransformComponent(Vector2.Zero));

        var bake = w.CreateEntity();
        bake.Set(new BakedProductComponent());
        bake.Set(new TransformComponent(Vector2.Zero));

        var tagged = LevelImporter.TagContentRoots(w);

        Assert.Equal(1, tagged); // only the top-level content root
        Assert.True(content.Has<SceneObjectComponent>());
        Assert.False(child.Has<SceneObjectComponent>()); // descendant, not a root
        Assert.False(infra.Has<SceneObjectComponent>()); // editor infrastructure excluded
        Assert.False(bake.Has<SceneObjectComponent>());  // bake product excluded

        // Idempotent: a second call tags nothing new.
        Assert.Equal(0, LevelImporter.TagContentRoots(w));
    }
}
