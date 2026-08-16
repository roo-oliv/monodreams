using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// Pins a ROOT layout slot (<see cref="LayoutSlotComponent.IsRoot"/>) at an arbitrary screen
/// position instead of letting it stack in the implicit solver container. Pure data:
/// <see cref="Anchor"/> picks the reference point on the virtual screen and <see cref="Offset"/>
/// displaces the root from it, in layout pixels (X right, Y down) — so
/// <c>{ Anchor = TopLeft, Offset = (32, 24) }</c> reads "32 px in, 24 px down from the top-left
/// corner", and <c>{ Anchor = Center, Offset = Vector2.Zero }</c> reads "centred on screen".
///
/// <para>Two systems act on this component, in this order:
/// <see cref="AutoLayoutSystem"/> leaves a pinned root OUT of the implicit screen container's flow
/// (so N pinned roots never push each other around) and solves its subtree standalone;
/// <see cref="PinnedLayoutRootSystem"/> then writes the root's final position. That placement must
/// happen AFTER the solver and BEFORE <c>HierarchySystem</c> — see the ui premises.</para>
///
/// <para>Removing the component degrades gracefully: the slot reverts to an ordinary
/// screen-anchored root that stacks in the solver container at
/// <see cref="LayoutSlotComponent.Anchor"/> (which <see cref="AutoLayoutBuilder.CreatePinnedRoot"/>
/// seeds with the same anchor).</para>
/// </summary>
public struct PinnedLayoutRootComponent
{
    /// The reference point on the virtual screen the root is placed against. The root's own
    /// bounding box is aligned to that point the same way an anchored root is (e.g.
    /// <see cref="ScreenAnchor.TopRight"/> puts the root's right edge on the screen's right edge),
    /// then <see cref="Offset"/> is added.
    public ScreenAnchor Anchor;

    /// Displacement from <see cref="Anchor"/>, in layout pixels (X right, Y down).
    public Vector2 Offset;
}
