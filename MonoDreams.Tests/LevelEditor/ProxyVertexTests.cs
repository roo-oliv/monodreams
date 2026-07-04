using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the island-authoring Slice 2 proxy generalization: <c>ProxySyncSystem</c> keys its
/// family by <c>(kind, index)</c>, and a <see cref="ProxyBindingKind.ConvexVertex"/> proxy per
/// <c>ModelVertices</c> entry materializes while the convex family's own proxy (shape or vertex)
/// is selected — one click deep. Dragging a vertex proxy writes back exactly ONE model vertex
/// through <see cref="ColliderEditCommand"/> (one drag = one undo step), and a drag frame whose
/// result would break convexity is rejected (the loud-reject strategy). Names the live premise
/// "Convex colliders are vertex-edited through (kind, index) proxies; invalid shapes are
/// rejected loudly" in MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class ProxyVertexTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static Entity CreateCursor(World world, Vector2 worldPoint, bool pressed)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = worldPoint,
            VirtualPosition = worldPoint,
            LeftButton = pressed,
            LeftButtonPressed = pressed,
        });
        return cursor;
    }

    private static List<Entity> Proxies(EntitySet set)
    {
        var list = new List<Entity>();
        foreach (var e in set.GetEntities()) list.Add(e);
        return list;
    }

    private static Entity FindProxy(EntitySet set, ProxyBindingKind kind, int index = 0)
    {
        foreach (var e in set.GetEntities())
        {
            var binding = e.Get<GizmoProxyComponent>();
            if (binding.Kind == kind && binding.Index == index) return e;
        }
        return default;
    }

    // ---- Family lifecycle: entity selection = shape proxies only; selecting the shape proxy
    // materializes the per-vertex handles; deselection despawns everything ----

    [Fact]
    public void VertexProxies_MaterializeWhenTheConvexFamilyProxyIsSelected()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));
        owner.Set(new SelectedComponent());

        // Entity selected: the whole-shape proxy only — no vertex clutter.
        sync.Update(Edit());
        Assert.Equal(1, proxies.Count);
        var shapeProxy = FindProxy(proxies, ProxyBindingKind.ConvexColliderShape);
        Assert.True(shapeProxy.IsAlive);
        Assert.Equal(0, shapeProxy.Get<GizmoProxyComponent>().Index);

        // Shape proxy selected (the designer clicked the outline): vertex handles materialize —
        // one per ModelVertices entry, keyed (ConvexVertex, i).
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(4, proxies.Count); // shape + 3 vertices
        for (var i = 0; i < 3; i++)
            Assert.True(FindProxy(proxies, ProxyBindingKind.ConvexVertex, i).IsAlive);

        // A vertex proxy is positioned AT its world vertex (owner at (100,100), identity rot/scale).
        var v1 = FindProxy(proxies, ProxyBindingKind.ConvexVertex, 1);
        Assert.Equal(new Vector2(120, 100), v1.Get<TransformComponent>().Position);

        // Selecting a VERTEX proxy keeps the whole family (still one click inside the session).
        shapeProxy.Remove<SelectedComponent>();
        v1.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(4, proxies.Count);

        // Back to the owner: the vertex handles fold away, the shape proxy stays.
        v1.Remove<SelectedComponent>();
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(1, proxies.Count);
        Assert.True(FindProxy(proxies, ProxyBindingKind.ConvexColliderShape).IsAlive);

        // Deselect everything: the family despawns.
        owner.Remove<SelectedComponent>();
        sync.Update(Edit());
        Assert.Equal(0, proxies.Count);
    }

    [Fact]
    public void VertexCountChange_ResizesTheFamilyLive()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20),
        }));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        var shapeProxy = FindProxy(proxies, ProxyBindingKind.ConvexColliderShape);
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(5, proxies.Count); // shape + 4 vertices

        // Shrink the vertex list through the real command path (a delete-vertex edit).
        history.Push(ColliderEditCommand.ForConvex(owner, new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20),
        }));
        sync.Update(Edit());
        Assert.Equal(4, proxies.Count); // shape + 3 vertices — index 3 pruned

        // Undo grows it back.
        history.Undo();
        sync.Update(Edit());
        Assert.Equal(5, proxies.Count);
    }

    // ---- Vertex drag: writes exactly ONE model vertex, one undo step, world data refreshed ----

    [Fact]
    public void VertexDrag_WritesTheRightModelVertex_OneUndoStep()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        var shapeProxy = FindProxy(proxies, ProxyBindingKind.ConvexColliderShape);
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());

        // Select vertex 1 (world (120,100)) and press on its pivot — the move handle.
        var v1 = FindProxy(proxies, ProxyBindingKind.ConvexVertex, 1);
        shapeProxy.Remove<SelectedComponent>();
        v1.Set(new SelectedComponent());
        sync.Update(Edit());
        var cursor = CreateCursor(world, new Vector2(120, 100), pressed: true);
        gizmo.Update(Edit());

        // Drag by (6, -4) and release.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(126, 96);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        var convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new Vector2(0, 0), convex.ModelVertices[0]);   // untouched
        Assert.Equal(new Vector2(26, -4), convex.ModelVertices[1]); // the dragged vertex
        Assert.Equal(new Vector2(10, 15), convex.ModelVertices[2]); // untouched
        // Derived world data refreshed by the write-back (transform local pos = (100,100)).
        Assert.Equal(new Vector2(126, 96), convex.WorldVertices[1]);
        Assert.Equal(1, history.Count); // one drag = one undo step

        history.Undo();
        convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new Vector2(20, 0), convex.ModelVertices[1]);
        Assert.Equal(new Vector2(120, 100), convex.WorldVertices[1]);
    }

    // ---- Convexity: an invalid drag result is rejected (not applied); a valid one lands ----

    [Fact]
    public void VertexDrag_RejectsNonConvexResult_KeepsLastValidShape()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var square = new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20),
        };
        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new ConvexColliderComponent((Vector2[])square.Clone()));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        var shapeProxy = FindProxy(proxies, ProxyBindingKind.ConvexColliderShape);
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());

        // Select vertex 0 (world (0,0)); press its handle; drag INTO the square (15,15) — the
        // result is non-convex, so the frame is rejected and nothing is applied.
        var v0 = FindProxy(proxies, ProxyBindingKind.ConvexVertex, 0);
        shapeProxy.Remove<SelectedComponent>();
        v0.Set(new SelectedComponent());
        sync.Update(Edit());
        var cursor = CreateCursor(world, new Vector2(0, 0), pressed: true);
        gizmo.Update(Edit());

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(15, 15);
        gizmo.Update(Edit());
        Assert.Equal(square, owner.Get<ConvexColliderComponent>().ModelVertices);

        // Release: no valid frame was ever pushed → the transaction commits nothing.
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(0, history.Count);
        Assert.Equal(square, owner.Get<ConvexColliderComponent>().ModelVertices);

        // A valid drag on the same vertex (outward, stays convex) lands normally.
        input.WorldPosition = new Vector2(0, 0);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        input.LeftButtonReleased = false;
        gizmo.Update(Edit());
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(-5, -5);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        Assert.Equal(new Vector2(-5, -5), owner.Get<ConvexColliderComponent>().ModelVertices[0]);
        Assert.Equal(1, history.Count);
    }

    // ---- The pure convexity guard ----

    [Fact]
    public void IsConvex_AcceptsConvexAndCollinear_RejectsConcaveAndDegenerate()
    {
        // Triangle and square: convex.
        Assert.True(ProxyGeometry.IsConvex(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));
        Assert.True(ProxyGeometry.IsConvex(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 20), new Vector2(0, 20),
        }));

        // A collinear midpoint (a just-inserted vertex) is legal.
        Assert.True(ProxyGeometry.IsConvex(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));

        // A dart (one reflex vertex) is not.
        Assert.False(ProxyGeometry.IsConvex(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 5), new Vector2(10, 20),
        }));

        // Degenerate: all collinear, or too few points.
        Assert.False(ProxyGeometry.IsConvex(new[]
        {
            new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0),
        }));
        Assert.False(ProxyGeometry.IsConvex(new[] { new Vector2(0, 0), new Vector2(10, 0) }));
        Assert.False(ProxyGeometry.IsConvex(null));
    }

    // ---- Selection: a click near a vertex picks the VERTEX handle over the shape border ----

    [Fact]
    public void VertexHandle_WinsThePick_WhereItRidesTheShapeBorder()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var selection = new SelectionSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        var shapeProxy = FindProxy(proxies, ProxyBindingKind.ConvexColliderShape);
        owner.Remove<SelectedComponent>();
        shapeProxy.Set(new SelectedComponent());
        sync.Update(Edit());

        // Click exactly at vertex 0's world position: both the shape border and the vertex handle
        // are under the cursor; the vertex's dedicated pick depth wins deterministically.
        CreateCursor(world, new Vector2(100, 100), pressed: true);
        selection.Update(Edit());

        var v0 = FindProxy(proxies, ProxyBindingKind.ConvexVertex, 0);
        Assert.True(v0.Has<SelectedComponent>());
        Assert.False(shapeProxy.Has<SelectedComponent>());
    }
}
