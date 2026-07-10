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
using MonoDreams.Message;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the colliders-as-entities authoring actions (CE-C): <b>Add Collider ▸ Box / Polygon</b>
/// creates a CHILD collider ENTITY of the selection (auto-named, footprint-shaped via
/// <see cref="ColliderDefaults"/>, passive, selected after creation — a body may have N colliders, so
/// no "already present" guard); <b>−Col</b> DELETES the selected collider entity (the snapshotting
/// delete — the component-remove command retired); <b>Add Vertex</b> inserts a convex-legal edge
/// midpoint into the selected convex collider entity; and Delete on a vertex proxy deletes its vertex
/// (≥3 guard) — never the collider entity itself. Names the live premises "A collider is a first-class
/// editor entity…" and "A convex collider entity's vertices are edited through (kind, index) grip
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
            world, history, new SceneSerializer(registry));
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

    private static Entity SingleCollider<T>(World world) where T : class
    {
        Entity found = default;
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities()) found = e;
        return found;
    }

    // ---- The footprint default math (plan §5.1), exact ----

    [Fact]
    public void FootprintBounds_FeetOrigin_FullWidthBottomQuarter_FeetAnchored()
    {
        // Feet-origin (Y-sorted band convention): Position IS the feet point → the box hangs off
        // it: full width centred, bottom quarter, bottom edge AT the feet.
        Assert.Equal(new Rectangle(-16, -12, 32, 12), ColliderDefaults.FootprintBounds(FeetOriginSprite()));

        var scaled = FeetOriginSprite();
        scaled.Size = new Vector2(64, 96);
        Assert.Equal(new Rectangle(-32, -24, 64, 24), ColliderDefaults.FootprintBounds(scaled));

        Assert.Equal(ColliderDefaults.FallbackFootprint, ColliderDefaults.FootprintBounds(new SpriteInfoComponent()));
    }

    [Fact]
    public void BoxChild_CentersOnTheFootprint_AndFallbackIs32Square()
    {
        // The collider child's LOCAL centre is the footprint rect's centre; a box centered there with
        // the footprint SIZE reproduces the feet-anchored footprint (rect (-16,-12,32,12) → centre
        // (0,-6), size (32,12)).
        var (center, size) = ColliderDefaults.BoxChild(FeetOriginSprite());
        Assert.Equal(new Vector2(0, -6), center);
        Assert.Equal(new Vector2(32, 12), size);

        // A sprite-less parent → a 32×32 box centred on the parent origin.
        Assert.Equal(Vector2.Zero, ColliderDefaults.FallbackBoxChild.Center);
        Assert.Equal(new Vector2(32, 32), ColliderDefaults.FallbackBoxChild.Size);
    }

    [Fact]
    public void HexagonChild_IsConvex_CentredOnTheFootprintCentre()
    {
        var (center, verts) = ColliderDefaults.HexagonChild(FeetOriginSprite());
        Assert.Equal(new Vector2(0, -6), center);
        Assert.Equal(6, verts.Length);
        Assert.True(ProxyGeometry.IsConvex(verts));
        // Vertices are rebased to the centre: spanning ±half the footprint about the origin.
        Assert.Equal(-16f, verts.Min(v => v.X));
        Assert.Equal(16f, verts.Max(v => v.X));
        Assert.Equal(-6f, verts.Min(v => v.Y));
        Assert.Equal(6f, verts.Max(v => v.Y));

        var (fbCenter, fbVerts) = ColliderDefaults.FallbackHexagonChild();
        Assert.Equal(Vector2.Zero, fbCenter);
        Assert.True(ProxyGeometry.IsConvex(fbVerts));
    }

    // ---- Add Collider: a CHILD collider entity, footprint-placed, auto-named, selected, undoable ----

    [Fact]
    public void AddBoxCollider_CreatesFootprintChildColliderEntity_Selected_Undoable()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);
        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(new Vector2(50, 50)));
        parent.Set(FeetOriginSprite());
        parent.Set(new SelectedComponent());

        commands.AddBoxCollider(Edit());
        Assert.False(parent.Has<BoxColliderComponent>()); // the shape lives on the CHILD, not the parent

        var child = SingleCollider<BoxColliderComponent>(world);
        Assert.True(child.IsAlive);
        Assert.Equal(new Vector2(32, 12), child.Get<BoxColliderComponent>().Size);
        Assert.True(child.Get<BoxColliderComponent>().Passive); // a footprint is a passive static blocker
        Assert.Equal(new Vector2(0, -6), child.Get<TransformComponent>().Position); // footprint centre, parent-local
        // Auto-named via EntityInfoComponent (Type carries the label, like AddEmptyEntity's "Empty" —
        // the tree/inspector labeler reads Name ?? Type).
        Assert.Equal("BoxCollider", child.Get<EntityInfoComponent>().Type);
        Assert.Equal(parent, child.Get<ChildOfComponent>().Parent); // a CHILD (serializes in the parent's closure)
        Assert.False(child.Has<SceneObjectComponent>()); // not a save-root (auto-parented)
        Assert.True(child.Has<SelectedComponent>()); // selected after creation
        Assert.Equal(1, history.Count);

        // No "already present" guard: a body may have N colliders — a second Add makes a second child.
        commands.AddBoxCollider(Edit());
        using (var boxes = world.GetEntities().With<BoxColliderComponent>().AsSet())
            Assert.Equal(2, boxes.Count);
        Assert.Equal(2, history.Count);

        history.Undo();
        using (var boxes = world.GetEntities().With<BoxColliderComponent>().AsSet())
            Assert.Equal(1, boxes.Count);
        history.Undo();
        using (var boxes = world.GetEntities().With<BoxColliderComponent>().AsSet())
            Assert.Equal(0, boxes.Count);
        history.Redo();
        using (var boxes = world.GetEntities().With<BoxColliderComponent>().AsSet())
            Assert.Equal(1, boxes.Count);
    }

    [Fact]
    public void AddConvexCollider_CreatesFootprintHexagonChild_WorldDataDerived()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);
        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(new Vector2(50, 50)));
        parent.Set(FeetOriginSprite());
        parent.Set(new SelectedComponent());

        commands.AddConvexCollider(Edit());
        var child = SingleCollider<ConvexColliderComponent>(world);
        Assert.True(child.IsAlive);
        Assert.Equal("PolyCollider", child.Get<EntityInfoComponent>().Type);
        Assert.Equal(new Vector2(0, -6), child.Get<TransformComponent>().Position);
        Assert.True(child.Get<ConvexColliderComponent>().Passive);
        Assert.Equal(parent, child.Get<ChildOfComponent>().Parent);
        Assert.True(child.Has<SelectedComponent>());
        // The fresh collider's derived world data reflects its WORLD transform: the child sits at
        // parent (50,50) + local centre (0,-6) = world (50,44), and (identity rot/scale) each world
        // vertex is its model vertex + that world position (physics is frozen in Edit — the command
        // refreshes the derived data itself).
        var convex = child.Get<ConvexColliderComponent>();
        var childWorld = new Vector2(50, 50) + new Vector2(0, -6);
        Assert.Equal(new Vector2(50, 44), childWorld);
        for (var i = 0; i < convex.ModelVertices.Length; i++)
            Assert.Equal(convex.ModelVertices[i] + childWorld, convex.WorldVertices[i]);
        Assert.Equal(1, history.Count);

        history.Undo();
        using var set = world.GetEntities().With<ConvexColliderComponent>().AsSet();
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void AddBoxCollider_SpritelessParent_Uses32SquareFallback()
    {
        using var world = new World();
        var (commands, _) = NewCommands(world);
        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(new Vector2(10, 10)));
        parent.Set(new EntityInfoComponent("Empty"));
        parent.Set(new SelectedComponent());

        commands.AddBoxCollider(Edit());
        var child = SingleCollider<BoxColliderComponent>(world);
        Assert.Equal(new Vector2(32, 32), child.Get<BoxColliderComponent>().Size);
        Assert.Equal(Vector2.Zero, child.Get<TransformComponent>().Position); // centred on the parent origin
    }

    // ---- Bug 2 behaviour: an Add-Collider footprint blocks an active body without drifting ----

    /// <summary>
    /// The footprint child the <c>+Box</c> action adds is a PASSIVE static blocker: an active body
    /// walking into it is stopped, and neither the footprint child nor its parent is pushed by the
    /// resolution (colliders-as-entities: the collider is a CHILD entity now). Drives the REAL collision
    /// + physics pipeline in-process (no window).
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

        // A static prop at (200,0) with a feet-origin sprite. +Box adds a CHILD footprint collider at
        // parent-local (0,-6) → world centre (200,-6), size 32×12 → world box x∈[184,216], y∈[-12,0].
        var prop = world.CreateEntity();
        prop.Set(new EntityInfoComponent("Prop", "tree"));
        prop.Set(new TransformComponent(new Vector2(200, 0)));
        prop.Set(FeetOriginSprite());
        prop.Set(new SelectedComponent());
        commands.AddBoxCollider(Edit());
        var footprint = SingleCollider<BoxColliderComponent>(world);
        Assert.True(footprint.Get<BoxColliderComponent>().Passive); // the fix under test

        // An ACTIVE player box moving right, straddling the footprint's y-band.
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        player.Set(new TransformComponent(new Vector2(100, 0)));
        player.Set(new BoxColliderComponent(new Vector2(16, 16))); // non-passive, centered
        player.Set(new VelocityComponent(new Vector2(15, 0)));

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
        // The static footprint child (and its parent prop) never moved.
        Assert.Equal(new Vector2(200, 0), prop.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(0, -6), footprint.Get<TransformComponent>().Position);
    }

    // ---- −Col: deletes the selected collider ENTITY (the snapshotting delete), undoable ----

    [Fact]
    public void RemoveCollider_DeletesTheSelectedColliderEntity_Undoable()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);

        var collider = world.CreateEntity();
        collider.Set(new EntityInfoComponent("BoxCollider"));
        collider.Set(new TransformComponent(new Vector2(7, 9)));
        collider.Set(new BoxColliderComponent(new Vector2(3, 4), new HashSet<int> { 1, 2 }, passive: true, enabled: false));
        collider.Set(new SelectedComponent());

        commands.RemoveCollider(Edit());
        Assert.False(collider.IsAlive); // the whole collider entity is deleted
        Assert.Equal(1, history.Count);

        history.Undo();
        var restored = SingleCollider<BoxColliderComponent>(world);
        Assert.True(restored.IsAlive);
        Assert.Equal(new Vector2(3, 4), restored.Get<BoxColliderComponent>().Size);
        Assert.True(restored.Get<BoxColliderComponent>().Passive);
        Assert.False(restored.Get<BoxColliderComponent>().Enabled);

        history.Redo();
        using var set = world.GetEntities().With<BoxColliderComponent>().AsSet();
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void RemoveCollider_WithNonColliderSelection_IsLoudNoOp()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);
        var plain = world.CreateEntity();
        plain.Set(new TransformComponent(Vector2.Zero));
        plain.Set(new EntityInfoComponent("Plain"));
        plain.Set(new SelectedComponent());

        commands.RemoveCollider(Edit());
        Assert.True(plain.IsAlive); // a non-collider selection is not deleted by −Col
        Assert.Equal(0, history.Count);
    }

    // ---- Delete is proxy-aware: a vertex proxy deletes its vertex (≥3 guard), not the entity ----

    [Fact]
    public void Delete_OnVertexProxy_DeletesTheVertex_WithMinThreeGuard()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var (commands, history) = NewCommands(world);

        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(Vector2.Zero));
        collider.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20),
        }));
        collider.Set(new SelectedComponent());
        sync.Update(Edit()); // grips materialize on selecting the collider entity

        Entity v2 = default;
        foreach (var p in proxies.GetEntities())
        {
            var binding = p.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex && binding.Index == 2) v2 = p;
        }
        collider.Remove<SelectedComponent>();
        v2.Set(new SelectedComponent());

        commands.DeleteSelection(Edit());
        var convex = collider.Get<ConvexColliderComponent>();
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(0, 20) }, convex.ModelVertices);
        Assert.Equal(1, history.Count);
        Assert.True(collider.IsAlive); // never a whole-entity delete
        // The selection moved back to the collider entity: the vertex-editing session continues.
        Assert.True(collider.Has<SelectedComponent>());
        sync.Update(Edit());

        // At 3 vertices the guard refuses (a convex collider keeps ≥ 3): loud no-op.
        Entity v0 = default;
        foreach (var p in proxies.GetEntities())
        {
            var binding = p.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex && binding.Index == 0) v0 = p;
        }
        collider.Remove<SelectedComponent>();
        v0.Set(new SelectedComponent());
        commands.DeleteSelection(Edit());
        Assert.Equal(3, collider.Get<ConvexColliderComponent>().ModelVertices.Length);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(4, collider.Get<ConvexColliderComponent>().ModelVertices.Length);
        Assert.Equal(new Vector2(20, 20), collider.Get<ConvexColliderComponent>().ModelVertices[2]);
    }

    [Fact]
    public void Delete_OnColliderEntity_DeletesTheWholeEntity()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);

        var collider = world.CreateEntity();
        collider.Set(new EntityInfoComponent("BoxCollider"));
        collider.Set(new TransformComponent(Vector2.Zero));
        collider.Set(new BoxColliderComponent(new Vector2(10, 12)));
        collider.Set(new SelectedComponent());

        commands.DeleteSelection(Edit());
        Assert.False(collider.IsAlive); // a collider entity deletes whole (the snapshotting delete)
        Assert.Equal(1, history.Count);

        history.Undo();
        var restored = SingleCollider<BoxColliderComponent>(world);
        Assert.Equal(new Vector2(10, 12), restored.Get<BoxColliderComponent>().Size);
    }

    [Fact]
    public void Delete_OnBakedProduct_IsRefused()
    {
        using var world = new World();
        var (commands, history) = NewCommands(world);

        var segment = world.CreateEntity();
        segment.Set(new TransformComponent(Vector2.Zero));
        segment.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(5, 4),
        }, passive: true));
        segment.Set(new BakedProductComponent());
        segment.Set(new SelectedComponent());

        commands.DeleteSelection(Edit());
        Assert.True(segment.IsAlive); // a baked product regenerates — Delete refuses
        Assert.Equal(0, history.Count);
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

        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(Vector2.Zero));
        collider.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(30, 0), new Vector2(15, 10),
        }));
        collider.Set(new SelectedComponent());

        // Collider entity selected: the longest edge (v0→v1, length 30) is split at its midpoint.
        commands.AddVertex(Edit());
        var convex = collider.Get<ConvexColliderComponent>();
        Assert.Equal(new[]
        {
            new Vector2(0, 0), new Vector2(15, 0), new Vector2(30, 0), new Vector2(15, 10),
        }, convex.ModelVertices);
        Assert.True(ProxyGeometry.IsConvex(convex.ModelVertices)); // collinear midpoint = legal
        Assert.Equal(1, history.Count);

        // A selected VERTEX proxy splits the edge AFTER that vertex instead.
        sync.Update(Edit());
        Entity v2 = default;
        foreach (var p in proxies.GetEntities())
        {
            var binding = p.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex && binding.Index == 2) v2 = p;
        }
        collider.Remove<SelectedComponent>();
        v2.Set(new SelectedComponent());

        commands.AddVertex(Edit()); // splits v2→v3: midpoint of (30,0)-(15,10) = (22.5, 5)
        Assert.Equal(new[]
        {
            new Vector2(0, 0), new Vector2(15, 0), new Vector2(30, 0),
            new Vector2(22.5f, 5), new Vector2(15, 10),
        }, collider.Get<ConvexColliderComponent>().ModelVertices);
        Assert.Equal(2, history.Count);

        history.Undo();
        Assert.Equal(4, collider.Get<ConvexColliderComponent>().ModelVertices.Length);
        history.Undo();
        Assert.Equal(3, collider.Get<ConvexColliderComponent>().ModelVertices.Length);
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
        using var boxes = world.GetEntities().With<BoxColliderComponent>().AsSet();
        Assert.Equal(0, boxes.Count);
        Assert.True(entity.IsAlive);
    }
}
