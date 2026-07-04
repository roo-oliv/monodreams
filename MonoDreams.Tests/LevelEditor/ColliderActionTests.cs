using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the island-authoring Slice 2 collider authoring actions (plan §5.1): Add box
/// collider applies the footprint default (full rendered width × bottom quarter, feet-anchored —
/// pure <see cref="ColliderDefaults"/>), Add polygon collider a footprint-inscribed hexagon,
/// Remove collider snapshots the removed component so undo restores it field-for-field, Add
/// vertex inserts a convex-legal edge midpoint, and Delete is proxy-aware (a vertex proxy
/// deletes its vertex with the ≥3 guard; a shape proxy removes its collider — never the
/// transient proxy entity). Names the live premises "Prop footprints default to full width ×
/// bottom quarter, feet-anchored" and "Convex colliders are vertex-edited through (kind, index)
/// proxies…" in MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class ColliderActionTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static (EditorCommandSystem commands, EditorHistory history) NewCommands(World world)
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var history = new EditorHistory(world);
        var commands = new EditorCommandSystem(
            world, history, new SceneSerializer(registry),
            deleteRequested: _ => false, undoRequested: _ => false, redoRequested: _ => false);
        return (commands, history);
    }

    /// <summary>A feet-origin prop sprite: 32×48 source rendered 1:1, Origin bottom-center.</summary>
    private static SpriteInfoComponent FeetOriginSprite() => new()
    {
        Source = new Rectangle(0, 0, 32, 48),
        Size = new Vector2(32, 48),
        Origin = new Vector2(16, 48),
        Target = RenderTargetID.Main,
    };

    // ---- The footprint default math (plan §5.1), exact ----

    [Fact]
    public void FootprintBounds_FeetOrigin_FullWidthBottomQuarter_FeetAnchored()
    {
        // Feet-origin (Y-sorted band convention): Position IS the feet point → the box hangs off
        // it: full width centred, bottom quarter, bottom edge AT the feet.
        Assert.Equal(new Rectangle(-16, -12, 32, 12), ColliderDefaults.FootprintBounds(FeetOriginSprite()));

        // Top-left origin (plain band): the quad spans (0,0)..(32,48) → footprint (0,36,32,12).
        var topLeft = FeetOriginSprite();
        topLeft.Origin = Vector2.Zero;
        Assert.Equal(new Rectangle(0, 36, 32, 12), ColliderDefaults.FootprintBounds(topLeft));

        // A render-scaled sprite (source 32×48 drawn at 64×96): the footprint follows the
        // RENDERED size, and the source-pixel Origin is scaled into rendered units.
        var scaled = FeetOriginSprite();
        scaled.Size = new Vector2(64, 96);
        Assert.Equal(new Rectangle(-32, -24, 64, 24), ColliderDefaults.FootprintBounds(scaled));

        // No usable size at all → the fallback feet-anchored box.
        Assert.Equal(ColliderDefaults.FallbackFootprint, ColliderDefaults.FootprintBounds(new SpriteInfoComponent()));
    }

    [Fact]
    public void FootprintHexagon_InscribedInTheFootprint_AndConvex()
    {
        var hexagon = ColliderDefaults.FootprintHexagon(FeetOriginSprite());
        Assert.Equal(6, hexagon.Length);
        Assert.True(ProxyGeometry.IsConvex(hexagon));
        // Inscribed in (-16,-12,32,12): x spans [-16,16], y spans [-12,0].
        Assert.Equal(-16f, hexagon.Min(v => v.X));
        Assert.Equal(16f, hexagon.Max(v => v.X));
        Assert.Equal(-12f, hexagon.Min(v => v.Y));
        Assert.Equal(0f, hexagon.Max(v => v.Y));
    }

    // ---- Add box / polygon: footprint applied, undoable, already-present guard ----

    [Fact]
    public void AddBoxCollider_AppliesFootprintDefault_Undoable()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(50, 50)));
        entity.Set(FeetOriginSprite());
        entity.Set(new SelectedComponent());

        commands.AddBoxCollider(Edit());
        Assert.True(entity.Has<BoxColliderComponent>());
        var box = entity.Get<BoxColliderComponent>();
        Assert.Equal(new Rectangle(-16, -12, 32, 12), box.Bounds);
        // A footprint is a PASSIVE static blocker (ColliderDefaults.FootprintPassive): Passive=true
        // = "does not initiate a collision", so a static prop blocks the player without being pushed
        // by resolution. (The Slice-2 assertion here was Assert.False — it asserted the wrong thing:
        // a Passive=false footprint drifts when the player walks into it. Bug 2 fix, Slice 3.5.)
        Assert.True(box.Passive);
        Assert.True(box.Enabled);
        Assert.Equal(1, history.Count);

        // Already present → loud no-op, no new history entry.
        commands.AddBoxCollider(Edit());
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.False(entity.Has<BoxColliderComponent>());
        history.Redo();
        Assert.Equal(new Rectangle(-16, -12, 32, 12), entity.Get<BoxColliderComponent>().Bounds);
    }

    [Fact]
    public void AddConvexCollider_AppliesFootprintHexagon_Undoable()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(50, 50)));
        entity.Set(FeetOriginSprite());
        entity.Set(new SelectedComponent());

        commands.AddConvexCollider(Edit());
        Assert.True(entity.Has<ConvexColliderComponent>());
        var convex = entity.Get<ConvexColliderComponent>();
        Assert.Equal(ColliderDefaults.FootprintHexagon(FeetOriginSprite()), convex.ModelVertices);
        Assert.True(convex.Passive); // a footprint is a passive static blocker (Bug 2 fix, Slice 3.5)
        // The fresh collider's derived world data reflects the entity's transform (physics is
        // frozen in Edit — the command refreshes it itself).
        Assert.Equal(new Vector2(50 + -8, 50 + -12), convex.WorldVertices[0]);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.False(entity.Has<ConvexColliderComponent>());
    }

    // ---- Bug 2 behaviour: an editor-added footprint blocks an active body without drifting ----

    /// <summary>
    /// The footprint the <c>+Box</c> action adds is a PASSIVE static blocker: an active body walking
    /// into it is stopped, and the footprint owner is NOT pushed by the resolution (mirrors the
    /// walkable-island milestone's building-footprint blocking assertion). Before the Bug 2 fix the
    /// footprint was <c>Passive=false</c> → it initiated a collision and DRIFTED away from the player.
    /// Drives the REAL collision + physics pipeline in-process (no window).
    /// </summary>
    [Fact]
    public void AddBoxFootprint_IsPassiveStaticBlocker_BlocksActiveBodyWithoutDrifting()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        var (commands, _) = NewCommands(world);

        // Detection auto-tags colliders on their component-ADDED event, so it must exist BEFORE any
        // collider is created (mirrors the real screen + the milestone).
        var velocity = new TransformVelocitySystem(world, runner);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, MilestoneCollision.Create);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);

        // A static prop at (200,0) with a feet-origin sprite → footprint world box x∈[184,216],
        // y∈[-12,0]. The +Box action gives it a passive footprint (Bug 2 fix).
        var prop = world.CreateEntity();
        prop.Set(new EntityInfoComponent("Prop", "tree"));
        prop.Set(new TransformComponent(new Vector2(200, 0)));
        prop.Set(FeetOriginSprite());
        prop.Set(new SelectedComponent());
        commands.AddBoxCollider(Edit());
        Assert.True(prop.Get<BoxColliderComponent>().Passive); // the fix under test

        // An ACTIVE player box moving right, straddling the footprint's y-band.
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        player.Set(new TransformComponent(new Vector2(100, 0)));
        player.Set(new BoxColliderComponent(new Rectangle(-8, -8, 16, 16))); // non-passive
        player.Set(new VelocityComponent(new Vector2(15, 0)));

        // Time = 1s so a velocity of v moves v units per stepped frame.
        var play = new GameState(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };
        for (var i = 0; i < 15; i++)
        {
            velocity.Update(play);
            detect.Update(play);
            resolve.Update(play);
            commit.Update(play);
        }

        var playerX = player.Get<TransformComponent>().Position.X;
        Assert.True(playerX < 184, $"player should be blocked before the footprint's left edge (184), was X={playerX}");
        Assert.True(playerX > 100, $"player should have advanced from its start (walked toward the prop), was X={playerX}");
        // The static footprint never moved (a Passive=false footprint would have drifted right).
        Assert.Equal(new Vector2(200, 0), prop.Get<TransformComponent>().Position);
    }

    // ---- Remove: snapshots restore the component field-for-field ----

    [Fact]
    public void RemoveCollider_Both_OneUndoEntry_RestoresFieldForField()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(7, 9)));
        entity.Set(new BoxColliderComponent(new Rectangle(1, 2, 3, 4),
            new HashSet<int> { 1, 2 }, passive: true, enabled: false));
        var vertices = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(5, 8) };
        entity.Set(new ConvexColliderComponent((Vector2[])vertices.Clone(),
            new HashSet<int> { 3 }, passive: true, enabled: true, ignoreTransformRotation: true));
        entity.Set(new SelectedComponent());

        commands.RemoveCollider(Edit());
        Assert.False(entity.Has<BoxColliderComponent>());
        Assert.False(entity.Has<ConvexColliderComponent>());
        Assert.Equal(1, history.Count); // both removals = ONE composite undo entry

        history.Undo();
        var box = entity.Get<BoxColliderComponent>();
        Assert.Equal(new Rectangle(1, 2, 3, 4), box.Bounds);
        Assert.True(box.ActiveLayers.SetEquals(new[] { 1, 2 }));
        Assert.True(box.Passive);
        Assert.False(box.Enabled);

        var convex = entity.Get<ConvexColliderComponent>();
        Assert.Equal(vertices, convex.ModelVertices);
        Assert.True(convex.ActiveLayers.SetEquals(new[] { 3 }));
        Assert.True(convex.Passive);
        Assert.True(convex.Enabled);
        Assert.True(convex.IgnoreTransformRotation);
        // Derived world data was refreshed against the live transform on restore.
        Assert.Equal(new Vector2(7, 9), convex.WorldVertices[0]);

        history.Redo();
        Assert.False(entity.Has<BoxColliderComponent>());
        Assert.False(entity.Has<ConvexColliderComponent>());
    }

    [Fact]
    public void RemoveCollider_ViaProxy_RemovesOnlyThatKind_ReselectsOwner()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var (commands, history) = NewCommands(world);

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new BoxColliderComponent(new Rectangle(0, 0, 10, 10)));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(5, 8),
        }));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());

        Entity boxProxy = default;
        foreach (var p in proxies.GetEntities())
            if (p.Get<GizmoProxyComponent>().Kind == ProxyBindingKind.BoxColliderBounds)
                boxProxy = p;
        owner.Remove<SelectedComponent>();
        boxProxy.Set(new SelectedComponent());

        commands.RemoveCollider(Edit());
        Assert.False(owner.Has<BoxColliderComponent>());   // the proxy's bound kind
        Assert.True(owner.Has<ConvexColliderComponent>()); // the other collider survives
        Assert.Equal(1, history.Count);
        Assert.True(owner.Has<SelectedComponent>()); // the session continues on the owner

        history.Undo();
        Assert.True(owner.Has<BoxColliderComponent>());
    }

    // ---- Delete is proxy-aware ----

    [Fact]
    public void Delete_OnVertexProxy_DeletesTheVertex_WithMinThreeGuard()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var (commands, history) = NewCommands(world);

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20),
        }));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        var shapeProxy = default(Entity);
        foreach (var p in proxies.GetEntities()) shapeProxy = p;
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());

        // Select vertex 2 and delete it.
        Entity v2 = default;
        foreach (var p in proxies.GetEntities())
        {
            var binding = p.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex && binding.Index == 2) v2 = p;
        }
        shapeProxy.Remove<SelectedComponent>();
        v2.Set(new SelectedComponent());

        commands.DeleteSelection(Edit());
        var convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(0, 20) },
            convex.ModelVertices);
        Assert.Equal(1, history.Count);
        Assert.True(owner.IsAlive); // never a whole-entity delete
        // The selection moved to the shape proxy: the vertex-editing session continues.
        Assert.True(shapeProxy.Has<SelectedComponent>());
        sync.Update(Edit());

        // At 3 vertices the guard refuses (a convex collider keeps ≥ 3): loud no-op.
        Entity v0 = default;
        foreach (var p in proxies.GetEntities())
        {
            var binding = p.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex && binding.Index == 0) v0 = p;
        }
        shapeProxy.Remove<SelectedComponent>();
        v0.Set(new SelectedComponent());
        commands.DeleteSelection(Edit());
        Assert.Equal(3, owner.Get<ConvexColliderComponent>().ModelVertices.Length);
        Assert.Equal(1, history.Count);

        // Undo restores the deleted vertex exactly.
        history.Undo();
        Assert.Equal(4, owner.Get<ConvexColliderComponent>().ModelVertices.Length);
        Assert.Equal(new Vector2(20, 20), owner.Get<ConvexColliderComponent>().ModelVertices[2]);
    }

    [Fact]
    public void Delete_OnShapeProxy_RemovesTheCollider_NotTheProxyEntity()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var (commands, history) = NewCommands(world);

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new BoxColliderComponent(new Rectangle(2, 3, 10, 12)));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Entity boxProxy = default;
        foreach (var p in proxies.GetEntities()) boxProxy = p;
        owner.Remove<SelectedComponent>();
        boxProxy.Set(new SelectedComponent());

        commands.DeleteSelection(Edit());
        Assert.True(owner.IsAlive);                       // the owner is never deleted
        Assert.False(owner.Has<BoxColliderComponent>());  // its collider is
        Assert.True(owner.Has<SelectedComponent>());      // and the selection lands on it
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(new Rectangle(2, 3, 10, 12), owner.Get<BoxColliderComponent>().Bounds);
    }

    // ---- Add vertex: edge midpoint, selected-vertex-aware, undoable ----

    [Fact]
    public void AddVertex_InsertsMidpoint_AfterSelectedVertex_OrIntoLongestEdge()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var (commands, history) = NewCommands(world);

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(30, 0), new Vector2(15, 10),
        }));
        owner.Set(new SelectedComponent());

        // Owner selected: the longest edge (v0→v1, length 30) is split at its midpoint.
        commands.AddVertex(Edit());
        var convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new[]
        {
            new Vector2(0, 0), new Vector2(15, 0), new Vector2(30, 0), new Vector2(15, 10),
        }, convex.ModelVertices);
        Assert.True(ProxyGeometry.IsConvex(convex.ModelVertices)); // collinear midpoint = legal
        Assert.Equal(1, history.Count);

        // A selected VERTEX proxy splits the edge AFTER that vertex instead.
        sync.Update(Edit());
        var shapeProxy = default(Entity);
        foreach (var p in proxies.GetEntities()) shapeProxy = p;
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());
        Entity v2 = default;
        foreach (var p in proxies.GetEntities())
        {
            var binding = p.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex && binding.Index == 2) v2 = p;
        }
        shapeProxy.Remove<SelectedComponent>();
        v2.Set(new SelectedComponent());

        commands.AddVertex(Edit()); // splits v2→v3: midpoint of (30,0)-(15,10) = (22.5, 5)
        Assert.Equal(new[]
        {
            new Vector2(0, 0), new Vector2(15, 0), new Vector2(30, 0),
            new Vector2(22.5f, 5), new Vector2(15, 10),
        }, owner.Get<ConvexColliderComponent>().ModelVertices);
        Assert.Equal(2, history.Count);

        history.Undo();
        Assert.Equal(4, owner.Get<ConvexColliderComponent>().ModelVertices.Length);
        history.Undo();
        Assert.Equal(3, owner.Get<ConvexColliderComponent>().ModelVertices.Length);
    }

    // ---- Guards: editing actions are Paused-only (loud in Play), and need a selection ----

    [Fact]
    public void Actions_InPlayOrWithoutSelection_AreLoudNoOps()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);

        // No selection at all.
        commands.AddBoxCollider(Edit());
        commands.RemoveCollider(Edit());
        commands.AddVertex(Edit());
        Assert.Equal(0, history.Count);

        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));
        entity.Set(FeetOriginSprite());
        entity.Set(new SelectedComponent());

        // Playing: every selection-edit action refuses (the transport owns the viewport).
        commands.AddBoxCollider(Play());
        commands.AddConvexCollider(Play());
        commands.RemoveCollider(Play());
        commands.AddVertex(Play());
        commands.DeleteSelection(Play());
        Assert.Equal(0, history.Count);
        Assert.False(entity.Has<BoxColliderComponent>());
        Assert.True(entity.IsAlive);
    }
}
