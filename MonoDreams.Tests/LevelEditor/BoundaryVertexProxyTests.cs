using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the boundary's per-vertex editing (island-authoring Slice 3): the Slice-2 (kind,index)
/// proxy machinery reused for <see cref="ProxyBindingKind.BoundaryVertex"/>. Handles materialize on
/// PLAIN selection of the boundary (a boundary IS its points — no shape proxy to click through);
/// dragging one writes back exactly one point (one undo step, re-fires the bake); delete keeps ≥ 2
/// points; add inserts an edge midpoint.
/// </summary>
public class BoundaryVertexProxyTests
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

    private static Entity FindProxy(EntitySet set, int index)
    {
        foreach (var e in set.GetEntities())
        {
            var binding = e.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.BoundaryVertex && binding.Index == index) return e;
        }
        return default;
    }

    private static int VertexProxyCount(EntitySet set)
    {
        var count = 0;
        foreach (var e in set.GetEntities())
            if (e.Get<GizmoProxyComponent>().Kind == ProxyBindingKind.BoundaryVertex) count++;
        return count;
    }

    private static Entity MakeBoundary(World world, Vector2[] localPoints, Vector2 position)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new BoundaryComponent(localPoints, 16f));
        return e;
    }

    private static (World, GameCamera, EditorHistory, ProxySyncSystem) Setup()
    {
        var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        var sync = new ProxySyncSystem(world, camera);
        var gizmoState = world.CreateEntity();
        gizmoState.Set(GizmoStateComponent.Default);
        return (world, camera, history, sync);
    }

    [Fact]
    public void VertexProxies_MaterializeOnPlainBoundarySelection_OnePerPoint()
    {
        var (world, _, _, sync) = Setup();
        using var _w = world;
        using var _s = sync;
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        // Boundary at Position=0 with 3 local points (== world here).
        var boundary = MakeBoundary(world,
            new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 40) }, Vector2.Zero);
        boundary.Set(new SelectedComponent());
        sync.Update(Edit());

        // 3 vertex proxies (one per point) plus the single thickness handle (Slice 4) = 4 total.
        Assert.Equal(3, VertexProxyCount(proxies));
        Assert.Equal(4, Proxies(proxies).Count);
        for (var i = 0; i < 3; i++) Assert.True(FindProxy(proxies, i).IsAlive);
        // A vertex proxy sits at its world point.
        Assert.Equal(new Vector2(40, 0), FindProxy(proxies, 1).Get<TransformComponent>().Position);

        // Selecting a vertex proxy keeps the family (anchor resolves to the boundary).
        boundary.Remove<SelectedComponent>();
        FindProxy(proxies, 1).Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(3, VertexProxyCount(proxies));

        // Deselect everything → despawn.
        foreach (var p in Proxies(proxies)) p.Remove<SelectedComponent>();
        sync.Update(Edit());
        Assert.Equal(0, Proxies(proxies).Count);
    }

    [Fact]
    public void VertexDrag_WritesTheRightPoint_OneUndoStep()
    {
        var (world, camera, history, sync) = Setup();
        using var _w = world;
        using var _s = sync;
        using var gizmo = new GizmoSystem(world, camera, history);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var boundary = MakeBoundary(world,
            new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 40) }, Vector2.Zero);
        boundary.Set(new SelectedComponent());
        sync.Update(Edit());

        // Select vertex 1 (world (40,0)); press its handle (the proxy pivot).
        var v1 = FindProxy(proxies, 1);
        boundary.Remove<SelectedComponent>();
        v1.Set(new SelectedComponent());
        sync.Update(Edit());
        var cursor = CreateCursor(world, new Vector2(40, 0), pressed: true);
        gizmo.Update(Edit());

        // Drag by (10, -5) and release.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(50, -5);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        var points = boundary.Get<BoundaryComponent>().Points;
        Assert.Equal(new Vector2(0, 0), points[0]);   // untouched
        Assert.Equal(new Vector2(50, -5), points[1]); // the dragged point
        Assert.Equal(new Vector2(40, 40), points[2]); // untouched
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(new Vector2(40, 0), boundary.Get<BoundaryComponent>().Points[1]);
    }

    [Fact]
    public void DeleteVertex_KeepsAtLeastTwoPoints()
    {
        var (world, _, history, sync) = Setup();
        using var _w = world;
        using var _s = sync;
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);
        using var commands = new EditorCommandSystem(
            world, history, serializer);

        var boundary = MakeBoundary(world,
            new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 40) }, Vector2.Zero);
        boundary.Set(new SelectedComponent());
        sync.Update(Edit());

        // Delete vertex 1 → 2 points remain (allowed).
        var v1 = FindProxy(proxies, 1);
        boundary.Remove<SelectedComponent>();
        v1.Set(new SelectedComponent());
        commands.DeleteSelection(Edit());
        Assert.Equal(2, boundary.Get<BoundaryComponent>().Points.Length);

        // A further delete is refused (a boundary keeps ≥ 2 points) — nothing pushed.
        var countBefore = history.Count;
        sync.Update(Edit()); // re-key the 2-point family (the delete auto-selected the boundary)
        boundary.Remove<SelectedComponent>(); // select ONLY the vertex proxy
        var v0 = FindProxy(proxies, 0);
        Assert.True(v0.IsAlive);
        v0.Set(new SelectedComponent());
        commands.DeleteSelection(Edit());
        Assert.Equal(2, boundary.Get<BoundaryComponent>().Points.Length);
        Assert.Equal(countBefore, history.Count);
    }

    [Fact]
    public void AddVertex_InsertsAnEdgeMidpoint()
    {
        var (world, _, history, sync) = Setup();
        using var _w = world;
        using var _s = sync;
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);
        using var commands = new EditorCommandSystem(
            world, history, serializer);

        var boundary = MakeBoundary(world,
            new[] { new Vector2(0, 0), new Vector2(40, 0) }, Vector2.Zero);
        boundary.Set(new SelectedComponent());

        commands.AddVertex(Edit());

        var points = boundary.Get<BoundaryComponent>().Points;
        Assert.Equal(3, points.Length);
        Assert.Equal(new Vector2(20, 0), points[1]); // midpoint of the only edge
    }
}
