using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the message-driven boundary bake (island-authoring Slice 3, plan §5.2 + wave-repass
/// §S2): <c>BoundaryBakeSystem</c> reacts to a <c>BoundaryComponent</c> being added/changed and
/// generates one thin convex quad segment collider per polyline edge, as <c>ChildOf</c> children
/// carrying <see cref="BakedProductComponent"/> — non-passive (they BLOCK), re-baked on edit,
/// never per-frame.
/// </summary>
public class BoundaryBakeTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static List<Entity> BakedChildren(World world, Entity boundary)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<BakedProductComponent>().With<ChildOfComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ChildOfComponent>().Parent == boundary)
                list.Add(e);
        return list;
    }

    private static Entity MakeBoundary(World world, Vector2[] localPoints, Vector2 position, float thickness = 16f)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new BoundaryComponent(localPoints, thickness));
        return e;
    }

    [Fact]
    public void ComponentAdded_Bakes_OneNonPassiveConvexSegmentPerEdge_AsMarkedChildOfChildren()
    {
        using var world = new World();
        using var bake = new BoundaryBakeSystem(world);

        // 4 local points → 3 edges → 3 segment quads. Set fires the ADDED subscription → enqueue.
        var points = new[] { new Vector2(-60, 0), new Vector2(-20, 0), new Vector2(20, 0), new Vector2(60, 0) };
        var boundary = MakeBoundary(world, points, new Vector2(100, 100), thickness: 20f);

        // Nothing baked until the queue drains in Update (event-driven, never per-frame).
        Assert.Empty(BakedChildren(world, boundary));

        bake.Update(Edit());

        var children = BakedChildren(world, boundary);
        Assert.Equal(3, children.Count);
        foreach (var child in children)
        {
            Assert.True(child.Has<ConvexColliderComponent>());
            // Passive = static world geometry (the WallEntityFactory idiom): it never initiates a
            // collision (so resolution never moves it) but the active player is pushed out of it.
            Assert.True(child.Get<ConvexColliderComponent>().Passive);
            Assert.Equal(boundary, child.Get<ChildOfComponent>().Parent);
            // The segment's collider sits at the boundary's world position (root-level collision).
            Assert.Equal(new Vector2(100, 100), child.Get<TransformComponent>().Position);
        }
    }

    [Fact]
    public void ComponentChanged_ReBakes_DisposingTheOldSegments()
    {
        using var world = new World();
        using var bake = new BoundaryBakeSystem(world);

        var boundary = MakeBoundary(world,
            new[] { new Vector2(0, 0), new Vector2(40, 0) }, Vector2.Zero); // 1 edge
        bake.Update(Edit());
        Assert.Single(BakedChildren(world, boundary));
        var firstPass = BakedChildren(world, boundary)[0];

        // Editing the polyline (a new component value) fires CHANGED → re-bake: 3 points → 2 edges.
        boundary.Set(new BoundaryComponent(
            new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 40) }, 16f));
        bake.Update(Edit());

        Assert.False(firstPass.IsAlive); // the old segment was disposed
        Assert.Equal(2, BakedChildren(world, boundary).Count);
    }

    [Fact]
    public void Bake_RunsInPlayMode_Too()
    {
        // A shipped game loading a scene must bake the boundary in Play (a scene-loading
        // participant, not Edit-only tooling — §S2).
        using var world = new World();
        using var bake = new BoundaryBakeSystem(world);
        var boundary = MakeBoundary(world,
            new[] { new Vector2(0, 0), new Vector2(30, 0), new Vector2(30, 30) }, Vector2.Zero);

        bake.Update(Play());

        Assert.Equal(2, BakedChildren(world, boundary).Count);
        Assert.Equal(1, bake.BakeCount);
    }

    [Fact]
    public void EmptyQueue_IsANoOp()
    {
        using var world = new World();
        using var bake = new BoundaryBakeSystem(world);
        bake.Update(Edit()); // no boundary added
        Assert.Equal(0, bake.BakeCount);
    }
}
