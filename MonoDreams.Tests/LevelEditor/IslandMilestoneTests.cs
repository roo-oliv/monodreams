using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Message;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// THE WALKABLE ISLAND MILESTONE (island-authoring open decision 6). An end-to-end, in-process test
/// over the REAL editor + engine systems (no window — the environment cannot present one; the
/// ledger documents the Cocoa hang): it builds an island scene with ≥2 buildings (footprint
/// colliders), ground + road patches, a coastline boundary, and ≥1 evidence + ≥1 talk-zone trigger;
/// saves it; reloads it via <see cref="LoadSceneRequest"/>; switches to Play and drives the player;
/// and asserts (1) the coastline BLOCKS the player, (2) a building footprint BLOCKS the player,
/// (3) entering each trigger fires a collision carrying the right <see cref="EntityInfoComponent"/>
/// identity, (4) the boundary's segment colliders are NEVER serialized yet REGENERATE on load, and
/// (5) a Restart-equivalent reload rebuilds the whole scene. Names the "walkable island" premise.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class IslandMilestoneTests
{
    private const string SceneFile = "island.scene.json";

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    // Time = 1s so a velocity of v moves v units per stepped frame.
    private static GameState Play() =>
        new(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };

    private static void WithPlatform(InMemoryPlatform fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static int CountWith<T>(World world)
    {
        using var set = world.GetEntities().With<T>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
    }

    private static Entity Single<T>(World world)
    {
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return default;
    }

    // ---- Scene-build helpers (the authored content) ----

    private static Entity MakeBuilding(World w, Vector2 pos)
    {
        var e = w.CreateEntity();
        e.Set(new EntityInfoComponent("Building", "building"));
        e.Set(new TransformComponent(pos));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/house", Source = new Rectangle(0, 0, 40, 56),
            Size = new Vector2(40, 56), Color = Color.White, Target = RenderTargetID.Main, LayerDepth = 0.5f,
        });
        // Footprint: a 40×40 base box, PASSIVE = static world geometry (the WallEntityFactory
        // idiom — it blocks the active player without being moved by the resolution).
        e.Set(new BoxColliderComponent(new Vector2(40, 40), passive: true));
        e.Set(new SceneObjectComponent());
        return e;
    }

    private static Entity MakePatch(World w, Vector2 pos, string name)
    {
        var e = w.CreateEntity();
        e.Set(new EntityInfoComponent("Patch", name));
        e.Set(new TransformComponent(pos));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/" + name, Source = new Rectangle(0, 0, 64, 64),
            Size = new Vector2(64, 64), Color = Color.White, Target = RenderTargetID.Main, LayerDepth = 0.1f,
        });
        e.Set(new SceneObjectComponent());
        return e;
    }

    private static Entity MakeTrigger(World w, TriggerType type, Vector2 pos, string name)
    {
        var e = TriggerFactory.Create(w, type, pos, name);
        e.Set(new SceneObjectComponent());
        return e;
    }

    [Fact]
    public void WalkableIslandMilestone()
    {
        var fake = new InMemoryPlatform();
        WithPlatform(fake, () =>
        {
            var evidence = new TriggerType("evidence", "Evidence", new Vector2(48, 48));
            var talkzone = new TriggerType("talkzone", "TalkZone", new Vector2(48, 48));

            // ============ BUILD (Edit) ============
            using var build = new World();
            var serializer = new SceneSerializer(NewRegistry());
            var history = new EditorHistory(build);
            var camera = new GameCamera(800, 600);
            using var buildBake = new BoundaryBakeSystem(build);
            using var boundaryTool = new BoundaryToolSystem(build, camera, history, serializer);
            var gizmoState = build.CreateEntity();
            gizmoState.Set(GizmoStateComponent.Default);

            // ≥2 buildings with footprint colliders.
            MakeBuilding(build, new Vector2(250, 100)); // in the player's row-1 path
            MakeBuilding(build, new Vector2(250, 250));
            // Ground patches + a road patch (dressing; sprites, no colliders).
            MakePatch(build, new Vector2(100, 100), "grass");
            MakePatch(build, new Vector2(300, 100), "sand");
            MakePatch(build, new Vector2(200, 200), "road");
            // ≥1 evidence + ≥1 talk-zone trigger.
            MakeTrigger(build, evidence, new Vector2(150, 100), "evidence_01"); // row-1 path
            MakeTrigger(build, talkzone, new Vector2(150, -50), "talkzone_01"); // row-2 path

            // A coastline boundary (a vertical wall) laid + committed through the real tool.
            boundaryTool.BeginBoundary();
            boundaryTool.LayVertex(new Vector2(400, -100));
            boundaryTool.LayVertex(new Vector2(400, 200));
            var boundary = boundaryTool.CommitBoundary();
            buildBake.Update(Edit()); // bake the segment collider(s)

            Assert.True(boundary.IsAlive);
            Assert.Equal(1, CountBaked(build)); // one edge → one segment quad

            // ============ SAVE ============
            new SceneWriter(serializer).Save(build, SceneFile, camera, layers: null);
            var saved = JsonSerializer.Deserialize<SceneData>(fake.Files[SceneFile]);
            Assert.NotNull(saved);

            // Never-serialize: the boundary root is written (with its polyline) but NO bake child is
            // (no convex collider appears — the segments regenerate on load).
            Assert.Equal(1, saved!.Entities.Count(e => e.Components.ContainsKey(EngineComponentSerializers.BoundaryKey)));
            Assert.DoesNotContain(saved.Entities, e => e.Components.ContainsKey(EngineComponentSerializers.ConvexColliderKey));
            // The full authored set round-trips: 2 buildings + 3 patches + 2 triggers + 1 boundary.
            Assert.Equal(8, saved.Entities.Count);
            Assert.Equal(2, saved.Entities.Count(e => Info(e) == "evidence" || Info(e) == "talkzone"));

            // ============ RELOAD (fresh world) + bake-on-load ============
            var play = ReloadAndBake(fake, out var reloadWorld, out var reloadRunner, out var detect);
            using var world = reloadWorld;
            using var runner = reloadRunner;

            // The boundary regenerated its segment collider on load (bake products regenerate).
            var reloadedBoundary = Single<BoundaryComponent>(world);
            Assert.True(reloadedBoundary.IsAlive);
            Assert.Equal(1, CountBaked(world));
            // The polyline round-tripped (local points preserved).
            var pts = reloadedBoundary.Get<BoundaryComponent>().Points;
            Assert.Equal(2, pts.Length);
            var worldPoly = BoundaryGeometry.WorldPolyline(pts, reloadedBoundary.Get<TransformComponent>().Position);
            Assert.Equal(new Vector2(400, -100), worldPoly[0]);
            Assert.Equal(new Vector2(400, 200), worldPoly[1]);

            // ============ PLAY: drive the player ============
            // Pre-mortem #1 tripwire: the player is a BODY with a CHILD collider entity. Resolution
            // must correct the BODY (player) so it walks the island; correcting the collider child
            // would drift it inside the parent and the block/advance assertions below would fail.
            var player = world.CreateEntity();
            player.Set(new EntityInfoComponent("Player"));
            player.Set(new TransformComponent(new Vector2(100, 100)));
            player.Set(new VelocityComponent(new Vector2(15, 0)));

            var playerCollider = world.CreateEntity();
            playerCollider.Set(new TransformComponent(Vector2.Zero)); // centered on the player body
            playerCollider.Set(new BoxColliderComponent(new Vector2(16, 16))); // non-passive
            playerCollider.SetParent(player);

            var hits = new List<CollisionMessage>();
            world.Subscribe((in CollisionMessage m) => hits.Add(m));

            var velocity = new TransformVelocitySystem(world, runner);
            var resolve = new TransformPhysicalCollisionResolutionSystem(world);
            var commit = new TransformCommitSystem(world, runner);

            void Step()
            {
                velocity.Update(play);
                detect.Update(play);
                resolve.Update(play);
                commit.Update(play);
            }

            // Row-1 pass: the player walks right, enters the evidence zone, then is BLOCKED by the
            // building footprint (x ∈ [230,270]).
            for (var i = 0; i < 20; i++) Step();
            Assert.True(player.Get<TransformComponent>().Position.X < 230,
                $"player should be blocked by the building footprint, was at X={player.Get<TransformComponent>().Position.X}");
            Assert.True(player.Get<TransformComponent>().Position.X > 118,
                "player should have advanced past its start (walked toward the building)");
            Assert.Contains(hits, m => Identity(m.ColliderB) == ("evidence", "evidence_01"));

            // Row-2 pass: teleport to a clear row, enter the talk zone, then be BLOCKED by the
            // coastline (baked segment at x ∈ [390,410]).
            hits.Clear();
            player.Get<TransformComponent>().Position = new Vector2(100, -50);
            player.Get<TransformComponent>().CommitPosition();
            player.Get<VelocityComponent>().Current = new Vector2(15, 0);
            for (var i = 0; i < 30; i++) Step();
            Assert.True(player.Get<TransformComponent>().Position.X < 390,
                $"player should be blocked by the coastline, was at X={player.Get<TransformComponent>().Position.X}");
            Assert.Contains(hits, m => Identity(m.ColliderB) == ("talkzone", "talkzone_01"));

            // ============ RESTART-equivalent: reload again rebuilds the whole scene ============
            // (For a native scene the transport's Restart re-publishes the original LoadSceneRequest,
            // which is what a fresh reload does.) The 8 authored roots reconstruct — counted by the
            // serialized EntityInfoComponent (SceneObjectComponent is transient editor state, added
            // at creation, not serialized) — and the coastline re-bakes.
            ReloadAndBake(fake, out var restartWorld, out var restartRunner, out _);
            using var restart = restartWorld;
            using var _r2 = restartRunner;
            Assert.Equal(8, CountWith<EntityInfoComponent>(restart)); // 2 buildings + 3 patches + 2 triggers + 1 boundary
            Assert.Equal(1, CountWith<BoundaryComponent>(restart));
            Assert.Equal(1, CountBaked(restart)); // coastline segments re-baked
        });
    }

    // Reloads the saved scene onto a fresh world (the boundary bake + collision detection systems
    // subscribe BEFORE the load, so the reconstructed components' added events reach them — the
    // coastline re-bakes and every loaded collider is auto-tagged). Returns the play GameState and
    // the detection system to drive; the world owns the rest.
    private static GameState ReloadAndBake(
        InMemoryPlatform fake, out World world, out DefaultParallelRunner runner,
        out TransformCollisionDetectionSystem<CollisionMessage> detect)
    {
        world = new World();
        runner = new DefaultParallelRunner(1);
        var serializer = new SceneSerializer(NewRegistry());
        // Subscribe BEFORE the load: BoundaryBakeSystem catches the boundary-added event, and the
        // detection system auto-tags every reconstructed collider (its component-added subscription).
        var bake = new BoundaryBakeSystem(world);
        detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, MilestoneCollision.Create);
        var reader = new SceneReaderSystem(world, serializer, content: null,
            loadTexture: _ => null, fileTextureLoader: _ => null);
        var play = Play();
        world.Publish(new LoadSceneRequest(SceneFile, fromContent: false));
        bake.Update(play); // drain the boundary-added bake request → regenerate the segments
        return play;
    }

    private static int CountBaked(World world)
    {
        using var set = world.GetEntities().With<BakedProductComponent>().With<ConvexColliderComponent>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
    }

    private static string Info(SceneEntityData e)
    {
        if (!e.Components.TryGetValue(EngineComponentSerializers.EntityInfoKey, out var el)) return "";
        return el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
    }

    private static (string, string) Identity(Entity e)
    {
        if (!e.IsAlive || !e.Has<EntityInfoComponent>()) return ("", "");
        var info = e.Get<EntityInfoComponent>();
        return (info.Type, info.Name);
    }
}
