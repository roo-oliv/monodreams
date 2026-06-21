#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// Pure C# layout node that provides flexbox-like layout capabilities.
/// </summary>
public class LayoutNodeComponent
{
    // Layout configuration
    public LayoutDirection FlexDirection { get; set; } = LayoutDirection.Vertical;
    public MainAxisAlignment JustifyContent { get; set; } = MainAxisAlignment.Start;
    public CrossAxisAlignment AlignItems { get; set; } = CrossAxisAlignment.Start;
    public float Gap { get; set; }

    // Padding
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }

    // Margin (applied by parent)
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }

    // Size
    public float? Width { get; set; }
    public float? Height { get; set; }
    public bool WidthAuto { get; set; } = true;
    public bool HeightAuto { get; set; } = true;
    public float FlexGrow { get; set; }

    // Per-axis "Fill container" (Figma) flags. Resolved by the PARENT, which is the only node
    // that knows the flow direction: on the parent's MAIN axis a fill child grows to share
    // leftover space (weighted by FlexGrow); on the parent's CROSS axis it stretches to the
    // parent's inner cross size. A fill axis still measures its BASE as hug-contents (see
    // MeasureSize) so the parent has a sensible starting size to grow/stretch from.
    public bool WidthFill { get; set; }
    public bool HeightFill { get; set; }

    // Computed layout results
    public float LayoutX { get; private set; }
    public float LayoutY { get; private set; }
    public float LayoutWidth { get; private set; }
    public float LayoutHeight { get; private set; }

    // Hierarchy
    public LayoutNodeComponent? Parent { get; set; }
    public List<LayoutNodeComponent> Children { get; } = [];

    // Debug
    public string? Name { get; set; }

    /// <summary>
    /// Adds a child node.
    /// </summary>
    public void AddChild(LayoutNodeComponent child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>
    /// Removes a child node.
    /// </summary>
    public void RemoveChild(LayoutNodeComponent child)
    {
        child.Parent = null;
        Children.Remove(child);
    }

    /// <summary>
    /// Clears all children.
    /// </summary>
    public void Clear()
    {
        foreach (var child in Children)
        {
            child.Parent = null;
        }
        Children.Clear();
    }

    /// <summary>
    /// Calculates layout for this node and all descendants.
    /// Call this on the root node after setting up the tree.
    /// </summary>
    public void CalculateLayout(float availableWidth = float.PositiveInfinity, float availableHeight = float.PositiveInfinity)
    {
        // First pass: measure sizes (bottom-up)
        MeasureSize(availableWidth, availableHeight);

        // Second pass: position children (top-down)
        LayoutX = 0;
        LayoutY = 0;
        PositionChildren();
    }

    /// <summary>
    /// Measures the size of this node based on its content and children.
    /// </summary>
    private void MeasureSize(float availableWidth, float availableHeight)
    {
        // Account for padding in available space for children
        var innerWidth = availableWidth - PaddingLeft - PaddingRight;
        var innerHeight = availableHeight - PaddingTop - PaddingBottom;

        // Measure all children first
        foreach (var child in Children)
        {
            child.MeasureSize(innerWidth, innerHeight);
        }

        // Calculate content size based on children
        float contentWidth = 0;
        float contentHeight = 0;

        if (Children.Count > 0)
        {
            if (FlexDirection == LayoutDirection.Horizontal)
            {
                // Horizontal: sum widths, max height
                for (int i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    contentWidth += child.LayoutWidth + child.MarginLeft + child.MarginRight;
                    if (i > 0) contentWidth += Gap;
                    contentHeight = MathHelper.Max(contentHeight, child.LayoutHeight + child.MarginTop + child.MarginBottom);
                }
            }
            else
            {
                // Vertical: max width, sum heights
                for (int i = 0; i < Children.Count; i++)
                {
                    var child = Children[i];
                    contentWidth = MathHelper.Max(contentWidth, child.LayoutWidth + child.MarginLeft + child.MarginRight);
                    contentHeight += child.LayoutHeight + child.MarginTop + child.MarginBottom;
                    if (i > 0) contentHeight += Gap;
                }
            }
        }

        // Determine final size. A "fill" axis measures its BASE as hug-contents here; the
        // parent's PositionChildren then grows it on the main axis (sharing leftover space)
        // or stretches it on the cross axis. WidthAuto without a fill flag also hugs. The bare
        // "else" (non-auto, non-fill, no explicit size) fills the available space — used by
        // the screen root, which is given an explicit size anyway.
        if (Width.HasValue)
            LayoutWidth = Width.Value;
        else if (WidthAuto || WidthFill)
            LayoutWidth = contentWidth + PaddingLeft + PaddingRight;
        else
            LayoutWidth = availableWidth;

        if (Height.HasValue)
            LayoutHeight = Height.Value;
        else if (HeightAuto || HeightFill)
            LayoutHeight = contentHeight + PaddingTop + PaddingBottom;
        else
            LayoutHeight = availableHeight;
    }

    /// <summary>
    /// Positions children within this node's bounds, resolving flex-grow on the main axis and
    /// Stretch / per-child cross-fill on the cross axis. Runs top-down: by the time a node is
    /// positioned, its own LayoutWidth/Height is final, so it can distribute its leftover space.
    /// </summary>
    private void PositionChildren()
    {
        if (Children.Count == 0) return;

        var horizontal = FlexDirection == LayoutDirection.Horizontal;
        var innerX = PaddingLeft;
        var innerY = PaddingTop;
        var innerWidth = LayoutWidth - PaddingLeft - PaddingRight;
        var innerHeight = LayoutHeight - PaddingTop - PaddingBottom;
        var mainAxisSize = horizontal ? innerWidth : innerHeight;
        var crossAxisSize = horizontal ? innerHeight : innerWidth;

        float MainOuter(LayoutNodeComponent c) => horizontal
            ? c.LayoutWidth + c.MarginLeft + c.MarginRight
            : c.LayoutHeight + c.MarginTop + c.MarginBottom;
        float CrossOuter(LayoutNodeComponent c) => horizontal
            ? c.LayoutHeight + c.MarginTop + c.MarginBottom
            : c.LayoutWidth + c.MarginLeft + c.MarginRight;
        bool MainFill(LayoutNodeComponent c) => horizontal ? c.WidthFill : c.HeightFill;
        bool CrossFill(LayoutNodeComponent c) => horizontal ? c.HeightFill : c.WidthFill;
        float GrowWeight(LayoutNodeComponent c) => c.FlexGrow > 0 ? c.FlexGrow : 1f;

        // --- Main-axis flex-grow: share leftover space among fill children by weight. ---
        float totalBase = 0;
        float totalGrow = 0;
        foreach (var child in Children)
        {
            totalBase += MainOuter(child);
            if (MainFill(child)) totalGrow += GrowWeight(child);
        }
        totalBase += Gap * (Children.Count - 1);
        var remainingSpace = mainAxisSize - totalBase;

        if (totalGrow > 0 && remainingSpace > 0)
        {
            foreach (var child in Children)
            {
                if (!MainFill(child)) continue;
                var add = remainingSpace * (GrowWeight(child) / totalGrow);
                if (horizontal) child.LayoutWidth += add; else child.LayoutHeight += add;
            }
            remainingSpace = 0; // grow consumed the slack; justify gets nothing left to spread
        }

        // --- Cross-axis: Stretch (all children) or per-child cross-fill grows to inner cross. ---
        foreach (var child in Children)
        {
            if (AlignItems != CrossAxisAlignment.Stretch && !CrossFill(child)) continue;
            if (horizontal)
                child.LayoutHeight = MathHelper.Max(0f, crossAxisSize - child.MarginTop - child.MarginBottom);
            else
                child.LayoutWidth = MathHelper.Max(0f, crossAxisSize - child.MarginLeft - child.MarginRight);
        }

        // --- Main-axis justification using any space the grow pass did not consume. ---
        float mainPos;
        float spacing = 0;
        switch (JustifyContent)
        {
            case MainAxisAlignment.Center:
                mainPos = remainingSpace / 2;
                break;
            case MainAxisAlignment.End:
                mainPos = remainingSpace;
                break;
            case MainAxisAlignment.SpaceBetween:
                mainPos = 0;
                if (Children.Count > 1) spacing = remainingSpace / (Children.Count - 1);
                break;
            case MainAxisAlignment.SpaceAround:
                spacing = remainingSpace / Children.Count;
                mainPos = spacing / 2;
                break;
            case MainAxisAlignment.SpaceEvenly:
                spacing = remainingSpace / (Children.Count + 1);
                mainPos = spacing;
                break;
            default: // Start
                mainPos = 0;
                break;
        }

        foreach (var child in Children)
        {
            var childCross = CrossOuter(child);

            // Stretched / cross-fill children already span the cross axis, so they pin to 0.
            float crossPos;
            if (AlignItems == CrossAxisAlignment.Stretch || CrossFill(child))
                crossPos = 0;
            else
                crossPos = AlignItems switch
                {
                    CrossAxisAlignment.Center => (crossAxisSize - childCross) / 2,
                    CrossAxisAlignment.End => crossAxisSize - childCross,
                    _ => 0,
                };

            if (horizontal)
            {
                child.LayoutX = innerX + mainPos + child.MarginLeft;
                child.LayoutY = innerY + crossPos + child.MarginTop;
            }
            else
            {
                child.LayoutX = innerX + crossPos + child.MarginLeft;
                child.LayoutY = innerY + mainPos + child.MarginTop;
            }

            mainPos += MainOuter(child) + Gap + spacing;

            child.PositionChildren();
        }
    }
}
