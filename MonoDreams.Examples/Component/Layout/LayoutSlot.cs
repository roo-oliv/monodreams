#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Layout;

namespace MonoDreams.Examples.Component.Layout;

/// <summary>
/// Component that marks an entity as a layout slot.
/// Layout slots are positioning anchors - their Transform is positioned by the layout system.
/// Content entities attach by setting Transform.Parent to the slot's transform.
/// </summary>
public class LayoutSlot
{
    public LayoutSlot(FlexLayoutNode? node = null)
    {
        Node = node ?? new FlexLayoutNode();
    }

    /// <summary>
    /// The underlying flexbox layout node.
    /// </summary>
    public FlexLayoutNode Node { get; }

    /// <summary>
    /// Optional reference to the attached content entity.
    /// Used for intrinsic size measurement.
    /// </summary>
    public Entity? Content { get; set; }

    /// <summary>
    /// Callback to measure the content entity's size.
    /// Invoked by IntrinsicSizingSystem when NeedsRemeasure is true.
    /// </summary>
    public Func<Entity, Vector2>? SizeMeasurer { get; set; }

    /// <summary>
    /// Whether this slot needs its size remeasured.
    /// </summary>
    public bool NeedsRemeasure { get; set; } = true;

    /// <summary>
    /// Whether this is a root layout slot (anchored to screen).
    /// </summary>
    public bool IsRoot { get; set; }

    /// <summary>
    /// Screen anchor position for root slots.
    /// </summary>
    public ScreenAnchor Anchor { get; set; } = ScreenAnchor.TopLeft;

    /// <summary>
    /// Which render target this UI element renders to.
    /// </summary>
    public RenderTargetID Target { get; set; }

    /// <summary>
    /// Computed X position after layout calculation.
    /// </summary>
    public float ComputedX => Node.LayoutX;

    /// <summary>
    /// Computed Y position after layout calculation.
    /// </summary>
    public float ComputedY => Node.LayoutY;

    /// <summary>
    /// Computed width after layout calculation.
    /// </summary>
    public float ComputedWidth => Node.LayoutWidth;

    /// <summary>
    /// Computed height after layout calculation.
    /// </summary>
    public float ComputedHeight => Node.LayoutHeight;
}
