using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Extension;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the foundation premise "<c>TransformComponent.IsDirty</c> cascades through the parent
/// chain" against the order-fragility that produced the level-editor's gizmo-vs-modal divergence
/// (viewport-tabs-2 §4): a parent's world-matrix cache clears its <c>IsDirty</c> flag as a side
/// effect of being READ, so a system that reads a moved parent's <c>WorldPosition</c> between the
/// edit and <c>HierarchySystem</c> used to erase the "my children are stale" signal — leaving the
/// child at its old world position. The fix is a read-stable
/// <see cref="TransformComponent.NeedsHierarchyUpdate"/> signal that only <c>HierarchySystem</c>
/// clears; these tests assert a child follows a parent move IDENTICALLY whether or not a
/// <c>WorldPosition</c> read intervenes (the gizmo path reads after <c>HierarchySystem</c>, the
/// modal path had a reader before it — they must agree).
///
/// Pure logic — a real in-memory world + the real <see cref="HierarchySystem"/>; no rendering.
/// </summary>
public class HierarchyDirtyPropagationTests
{
    private static GameState Frame() => new(new GameTime());

    /// <summary>Parent at <paramref name="parentPos"/> with one child at <paramref name="childLocal"/>,
    /// settled once through <see cref="HierarchySystem"/> so both world caches are established (the
    /// state a real screen is in before an edit).</summary>
    private static (Entity parent, Entity child) MakeSettledPair(
        World world, HierarchySystem hierarchy, Vector2 parentPos, Vector2 childLocal)
    {
        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(parentPos));

        var child = world.CreateEntity();
        child.Set(new TransformComponent(childLocal));
        child.SetParent(parent);

        hierarchy.Update(Frame());
        // Prime both caches (a real frame's renderer reads them), so a later stale read would surface.
        _ = parent.Get<TransformComponent>().WorldPosition;
        _ = child.Get<TransformComponent>().WorldPosition;
        return (parent, child);
    }

    // ── The regression: a WorldPosition read between the edit and HierarchySystem must NOT drop the child ──

    /// <summary>The modal path's pipeline order: edit the parent, then a system reads the parent's
    /// <c>WorldPosition</c> (as <c>ButtonMeshPrepSystem</c> does for a button's outline mesh) — which
    /// clears the parent's cache-dirty bit — then <c>HierarchySystem</c> runs. The child must still be
    /// re-dirtied and land at the new world position.</summary>
    [Fact]
    public void ChildFollowsParentMove_EvenWhenWorldPositionIsReadBeforeHierarchySystem()
    {
        using var world = new World();
        using var hierarchy = new HierarchySystem(world);
        var (parent, child) = MakeSettledPair(world, hierarchy, new Vector2(10, 20), new Vector2(5, 5));

        // Edit the parent (as a TransformEditCommand from the gizmo/modal does).
        parent.Get<TransformComponent>().Position = new Vector2(110, 20);

        // A reader consumes the parent's WorldPosition BEFORE HierarchySystem (the modal-order case).
        // This clears the parent's IsDirty cache bit — the exact side effect that used to drop the child.
        _ = parent.Get<TransformComponent>().WorldPosition;
        Assert.False(parent.Get<TransformComponent>().IsDirty);             // cache bit cleared by the read
        Assert.True(parent.Get<TransformComponent>().NeedsHierarchyUpdate); // but the propagation signal survives

        hierarchy.Update(Frame());

        // The child followed the +100 x move (would be (15,25) — stale — on the buggy path).
        Assert.Equal(new Vector2(115, 25), child.Get<TransformComponent>().WorldPosition);
    }

    /// <summary>Parity: the gizmo path (no read between the edit and <c>HierarchySystem</c>) and the
    /// modal path (a <c>WorldPosition</c> read in between) must leave the child at the SAME world
    /// position. This is the "a changed-child consumer observes a modal move exactly as it observes a
    /// gizmo drag" contract from viewport-tabs-2 §4, at the engine level that both paths share.</summary>
    [Fact]
    public void ChildFollow_IsIdentical_ForGizmoOrderAndModalOrder()
    {
        Vector2 RunWithReadBeforeHierarchy(bool readBefore)
        {
            using var world = new World();
            using var hierarchy = new HierarchySystem(world);
            var (parent, child) = MakeSettledPair(world, hierarchy, new Vector2(0, 0), new Vector2(7, -3));

            parent.Get<TransformComponent>().Position = new Vector2(40, 15);
            if (readBefore) _ = parent.Get<TransformComponent>().WorldPosition; // modal-order reader
            hierarchy.Update(Frame());
            return child.Get<TransformComponent>().WorldPosition;
        }

        var gizmoOrder = RunWithReadBeforeHierarchy(readBefore: false);
        var modalOrder = RunWithReadBeforeHierarchy(readBefore: true);

        Assert.Equal(new Vector2(47, 12), gizmoOrder);
        Assert.Equal(gizmoOrder, modalOrder); // the two paths agree — no divergence
    }

    /// <summary>A grandchild (two levels down) also follows when a read intervenes — the propagation
    /// walks the full subtree, so nested button labels / decorations track the root too.</summary>
    [Fact]
    public void GrandchildFollowsRootMove_WithInterveningRead()
    {
        using var world = new World();
        using var hierarchy = new HierarchySystem(world);

        var root = world.CreateEntity();
        root.Set(new TransformComponent(new Vector2(100, 100)));
        var mid = world.CreateEntity();
        mid.Set(new TransformComponent(new Vector2(10, 0)));
        mid.SetParent(root);
        var leaf = world.CreateEntity();
        leaf.Set(new TransformComponent(new Vector2(2, 2)));
        leaf.SetParent(mid);

        hierarchy.Update(Frame());
        _ = leaf.Get<TransformComponent>().WorldPosition; // prime

        root.Get<TransformComponent>().Position = new Vector2(200, 100); // +100 x
        _ = root.Get<TransformComponent>().WorldPosition;                // intervening read (clears root cache bit)
        hierarchy.Update(Frame());

        Assert.Equal(new Vector2(212, 102), leaf.Get<TransformComponent>().WorldPosition);
    }

    /// <summary>HierarchySystem clears the propagation signal each frame, so an unchanged transform
    /// does not keep re-propagating: after one settled frame with no edit, nothing is flagged.</summary>
    [Fact]
    public void PropagationSignal_IsClearedAfterEachHierarchyPass()
    {
        using var world = new World();
        using var hierarchy = new HierarchySystem(world);
        var (parent, child) = MakeSettledPair(world, hierarchy, new Vector2(1, 1), new Vector2(1, 1));

        // No edit this frame.
        hierarchy.Update(Frame());

        Assert.False(parent.Get<TransformComponent>().NeedsHierarchyUpdate);
        Assert.False(child.Get<TransformComponent>().NeedsHierarchyUpdate);
    }
}
