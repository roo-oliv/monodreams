using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the boundary <b>thickness handle</b> (island-authoring Slice 4): a single
/// <see cref="ProxyBindingKind.BoundaryThickness"/> proxy that rides the band edge and, when
/// dragged along the edge normal, changes <c>BoundaryComponent.Thickness</c> through one
/// <c>BoundaryEditCommand</c> (one undo step) which re-fires the bake.
/// </summary>
public class BoundaryThicknessTests
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

    private static Entity FindThicknessProxy(EntitySet set)
    {
        foreach (var e in set.GetEntities())
            if (e.Get<GizmoProxyComponent>().Kind == ProxyBindingKind.BoundaryThickness) return e;
        return default;
    }

    private static Entity MakeBoundary(World world, Vector2[] localPoints, Vector2 position, float thickness = 16f)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new BoundaryComponent(localPoints, thickness));
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
    public void ThicknessHandle_MaterializesOnBoundarySelection_AtTheBandEdge()
    {
        var (world, _, _, sync) = Setup();
        using var _w = world;
        using var _s = sync;
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        // Horizontal edge (0,0)→(40,0), thickness 16 → left-hand normal (0,1); the handle sits at
        // the edge midpoint (20,0) offset by normal × 8 = (20,8).
        var boundary = MakeBoundary(world, new[] { new Vector2(0, 0), new Vector2(40, 0) }, Vector2.Zero);
        boundary.Set(new SelectedComponent());
        sync.Update(Edit());

        var handle = FindThicknessProxy(proxies);
        Assert.True(handle.IsAlive);
        Assert.Equal(new Vector2(20, 8), handle.Get<TransformComponent>().Position);
    }

    [Fact]
    public void ThicknessHandleDrag_ChangesThickness_OneUndoStep_AndReBakes()
    {
        var (world, camera, history, sync) = Setup();
        using var _w = world;
        using var _s = sync;
        using var gizmo = new GizmoSystem(world, camera, history);
        using var bake = new BoundaryBakeSystem(world);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var boundary = MakeBoundary(world, new[] { new Vector2(0, 0), new Vector2(40, 0) }, Vector2.Zero);
        bake.Update(Edit()); // initial bake (the ADDED event) → BakeCount 1
        Assert.Equal(1, bake.BakeCount);

        boundary.Set(new SelectedComponent());
        sync.Update(Edit());
        var handle = FindThicknessProxy(proxies);
        Assert.True(handle.IsAlive);

        // Select the handle and press it (at its pivot (20,8)).
        boundary.Remove<SelectedComponent>();
        handle.Set(new SelectedComponent());
        sync.Update(Edit());
        var cursor = CreateCursor(world, new Vector2(20, 8), pressed: true);
        gizmo.Update(Edit());

        // Drag +4 along the normal (down in y): thickness += 2 × 4 = 8 → 24.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(20, 12);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        Assert.Equal(24f, boundary.Get<BoundaryComponent>().Thickness, 3);
        Assert.Equal(1, history.Count); // one drag = one undo step

        // The thickness change fired the bake (re-bakes the band to the new width).
        bake.Update(Edit());
        Assert.Equal(2, bake.BakeCount);

        // Undo restores the original thickness.
        history.Undo();
        Assert.Equal(16f, boundary.Get<BoundaryComponent>().Thickness, 3);
    }
}
