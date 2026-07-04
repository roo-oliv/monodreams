using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the boundary tool's lay/commit/cancel lifecycle (island-authoring Slice 3, plan §5.2):
/// a click-lay polyline commits as ONE undo step into a <c>BoundaryComponent</c> authoring entity
/// (pivot = the laid points' centroid, Points stored local to it), Escape/cancel leaves nothing,
/// and a lay with fewer than two points is discarded. Drives the tool's public API directly (the
/// same entry points the headless <c>boundary:*</c> ops and interactive clicks route through).
/// </summary>
public class BoundaryToolTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static (World world, EditorHistory history, BoundaryToolSystem tool, Entity gizmoState) Setup()
    {
        var world = new World();
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);
        var history = new EditorHistory(world);
        var camera = new GameCamera(800, 600);
        var tool = new BoundaryToolSystem(world, camera, history, serializer);
        var gizmoState = world.CreateEntity();
        gizmoState.Set(GizmoStateComponent.Default);
        return (world, history, tool, gizmoState);
    }

    private static Entity TheBoundary(World world)
    {
        using var set = world.GetEntities().With<BoundaryComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return default;
    }

    [Fact]
    public void LayCommit_CreatesBoundaryEntity_OneUndoStep_CentroidPivotLocalPoints()
    {
        var (world, history, tool, gizmoState) = Setup();
        using var _ = world;

        tool.BeginBoundary();
        Assert.Equal(EditorToolMode.Boundary, gizmoState.Get<GizmoStateComponent>().Mode);

        var worldPoints = new[] { new Vector2(0, 0), new Vector2(120, 0), new Vector2(120, 90) };
        foreach (var p in worldPoints) tool.LayVertex(p);
        Assert.Equal(3, tool.PendingCount);

        var boundary = tool.CommitBoundary();

        // One undo step for the whole lay.
        Assert.Equal(1, history.Count);
        // Back to select/transform after commit.
        Assert.Equal(EditorToolMode.SelectTransform, gizmoState.Get<GizmoStateComponent>().Mode);
        Assert.Equal(0, tool.PendingCount);

        // The authoring entity is correct: tagged save-root, EntityInfo("Boundary"), a
        // BoundaryComponent, and it is auto-selected.
        Assert.True(boundary.IsAlive);
        Assert.True(boundary.Has<SceneObjectComponent>());
        Assert.True(boundary.Has<SelectedComponent>());
        Assert.Equal("Boundary", boundary.Get<EntityInfoComponent>().Type);

        var component = boundary.Get<BoundaryComponent>();
        Assert.Equal(3, component.Points.Length);
        Assert.Equal(BoundaryComponent.DefaultThickness, component.Thickness);

        // Pivot = centroid; Points are local, so Position + Points reproduce the world polyline.
        var centroid = BoundaryGeometry.Centroid(worldPoints);
        Assert.Equal(centroid, boundary.Get<TransformComponent>().Position);
        var reconstructed = BoundaryGeometry.WorldPolyline(component.Points, boundary.Get<TransformComponent>().Position);
        for (var i = 0; i < worldPoints.Length; i++)
        {
            Assert.Equal(worldPoints[i].X, reconstructed[i].X, 3);
            Assert.Equal(worldPoints[i].Y, reconstructed[i].Y, 3);
        }
    }

    [Fact]
    public void Commit_FewerThanTwoPoints_DiscardsAndCreatesNothing()
    {
        var (world, history, tool, gizmoState) = Setup();
        using var _ = world;

        tool.BeginBoundary();
        tool.LayVertex(new Vector2(10, 10)); // only one point
        var result = tool.CommitBoundary();

        Assert.Equal(default, result);
        Assert.Equal(0, history.Count);
        Assert.Equal(default, TheBoundary(world));
        Assert.Equal(EditorToolMode.SelectTransform, gizmoState.Get<GizmoStateComponent>().Mode);
    }

    [Fact]
    public void Cancel_LeavesNothing_AndRestoresSelectTransform()
    {
        var (world, history, tool, gizmoState) = Setup();
        using var _ = world;

        tool.BeginBoundary();
        tool.LayVertex(new Vector2(0, 0));
        tool.LayVertex(new Vector2(50, 0));
        tool.CancelBoundary();

        Assert.Equal(0, tool.PendingCount);
        Assert.Equal(0, history.Count);
        Assert.Equal(default, TheBoundary(world));
        Assert.Equal(EditorToolMode.SelectTransform, gizmoState.Get<GizmoStateComponent>().Mode);
    }

    [Fact]
    public void Undo_OfACommit_DisposesTheBoundaryEntity()
    {
        var (world, history, tool, gizmoState) = Setup();
        using var _ = world;

        tool.BeginBoundary();
        tool.LayVertex(new Vector2(0, 0));
        tool.LayVertex(new Vector2(80, 0));
        tool.LayVertex(new Vector2(80, 60));
        var boundary = tool.CommitBoundary();
        Assert.True(boundary.IsAlive);

        history.Undo();
        Assert.False(boundary.IsAlive); // create → delete on undo
        Assert.Equal(default, TheBoundary(world));
    }
}
