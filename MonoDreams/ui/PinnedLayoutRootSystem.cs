using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Places every <see cref="PinnedLayoutRootComponent"/> root at its pinned position:
/// the root's anchor offset (same anchor grid <see cref="AutoLayoutSystem"/> uses, resolved
/// against the root's SOLVED size) plus the pin's own offset, written straight onto the root's
/// <c>TransformComponent</c>.
///
/// <para><b>Pipeline slot — this ordering IS the feature.</b> Register it AFTER
/// <see cref="AutoLayoutSystem"/> (the solver must have sized the root and positioned its subtree
/// before anything can place it) and BEFORE <c>HierarchySystem</c> (so the placement propagates to
/// the root's descendants in the same frame). The solver owns layout WITHIN a root; this system
/// owns WHERE the solved root sits.</para>
///
/// <para>The write is absolute, not relative: the position is recomputed from the anchor + offset
/// each frame, so it is idempotent and survives a viewport resize or a re-measure that changes the
/// root's size.</para>
/// </summary>
public sealed class PinnedLayoutRootSystem : ISystem<GameState>
{
    private readonly ViewportManager _viewport;
    private readonly EntitySet _pinnedRoots;

    public PinnedLayoutRootSystem(World world, ViewportManager viewport)
    {
        _viewport = viewport;
        _pinnedRoots = world.GetEntities()
            .With<PinnedLayoutRootComponent>()
            .With<LayoutSlotComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        foreach (var entity in _pinnedRoots.GetEntities())
        {
            ref readonly var slot = ref entity.Get<LayoutSlotComponent>();

            // Pinning is a ROOT-placement primitive: a non-root slot's position belongs to its
            // parent container, so pinning one would fight the solver every frame.
            if (!slot.IsRoot) continue;

            ref readonly var pin = ref entity.Get<PinnedLayoutRootComponent>();
            var anchorOffset = AutoLayoutSystem.GetScreenAnchorOffset(
                _viewport, pin.Anchor, slot.ComputedWidth, slot.ComputedHeight, slot.Target);

            entity.Get<TransformComponent>().Position = anchorOffset + pin.Offset;
        }
    }

    public void Dispose() => _pinnedRoots.Dispose();
}
