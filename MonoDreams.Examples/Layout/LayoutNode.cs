#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Layout;

/// <summary>
/// Pure C# layout node that provides flexbox-like layout capabilities.
/// Replaces Facebook.Yoga for cross-platform compatibility.
/// </summary>
public class FlexLayoutNode
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

    // Computed layout results
    public float LayoutX { get; private set; }
    public float LayoutY { get; private set; }
    public float LayoutWidth { get; private set; }
    public float LayoutHeight { get; private set; }

    // Hierarchy
    public FlexLayoutNode? Parent { get; set; }
    public List<FlexLayoutNode> Children { get; } = [];

    // Debug
    public string? Name { get; set; }

    /// <summary>
    /// Adds a child node.
    /// </summary>
    public void AddChild(FlexLayoutNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>
    /// Removes a child node.
    /// </summary>
    public void RemoveChild(FlexLayoutNode child)
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

        // Determine final size
        if (Width.HasValue)
        {
            LayoutWidth = Width.Value;
        }
        else if (WidthAuto)
        {
            LayoutWidth = contentWidth + PaddingLeft + PaddingRight;
        }
        else
        {
            LayoutWidth = availableWidth;
        }

        if (Height.HasValue)
        {
            LayoutHeight = Height.Value;
        }
        else if (HeightAuto)
        {
            LayoutHeight = contentHeight + PaddingTop + PaddingBottom;
        }
        else
        {
            LayoutHeight = availableHeight;
        }
    }

    /// <summary>
    /// Positions children within this node's bounds.
    /// </summary>
    private void PositionChildren()
    {
        if (Children.Count == 0) return;

        var innerX = PaddingLeft;
        var innerY = PaddingTop;
        var innerWidth = LayoutWidth - PaddingLeft - PaddingRight;
        var innerHeight = LayoutHeight - PaddingTop - PaddingBottom;

        // Calculate total content size and remaining space
        float totalMainSize = 0;
        float maxCrossSize = 0;

        foreach (var child in Children)
        {
            if (FlexDirection == LayoutDirection.Horizontal)
            {
                totalMainSize += child.LayoutWidth + child.MarginLeft + child.MarginRight;
                maxCrossSize = MathHelper.Max(maxCrossSize, child.LayoutHeight + child.MarginTop + child.MarginBottom);
            }
            else
            {
                totalMainSize += child.LayoutHeight + child.MarginTop + child.MarginBottom;
                maxCrossSize = MathHelper.Max(maxCrossSize, child.LayoutWidth + child.MarginLeft + child.MarginRight);
            }
        }

        // Add gaps to total
        totalMainSize += Gap * (Children.Count - 1);

        float mainAxisSize = FlexDirection == LayoutDirection.Horizontal ? innerWidth : innerHeight;
        float crossAxisSize = FlexDirection == LayoutDirection.Horizontal ? innerHeight : innerWidth;
        float remainingSpace = mainAxisSize - totalMainSize;

        // Calculate starting position and spacing based on JustifyContent
        float mainPos;
        float spacing = 0;

        switch (JustifyContent)
        {
            case MainAxisAlignment.Start:
                mainPos = 0;
                break;
            case MainAxisAlignment.Center:
                mainPos = remainingSpace / 2;
                break;
            case MainAxisAlignment.End:
                mainPos = remainingSpace;
                break;
            case MainAxisAlignment.SpaceBetween:
                mainPos = 0;
                if (Children.Count > 1)
                    spacing = remainingSpace / (Children.Count - 1);
                break;
            case MainAxisAlignment.SpaceAround:
                spacing = remainingSpace / Children.Count;
                mainPos = spacing / 2;
                break;
            case MainAxisAlignment.SpaceEvenly:
                spacing = remainingSpace / (Children.Count + 1);
                mainPos = spacing;
                break;
            default:
                mainPos = 0;
                break;
        }

        // Position each child
        foreach (var child in Children)
        {
            float childMainSize = FlexDirection == LayoutDirection.Horizontal
                ? child.LayoutWidth + child.MarginLeft + child.MarginRight
                : child.LayoutHeight + child.MarginTop + child.MarginBottom;

            float childCrossSize = FlexDirection == LayoutDirection.Horizontal
                ? child.LayoutHeight + child.MarginTop + child.MarginBottom
                : child.LayoutWidth + child.MarginLeft + child.MarginRight;

            // Calculate cross-axis position based on AlignItems
            float crossPos;
            switch (AlignItems)
            {
                case CrossAxisAlignment.Start:
                    crossPos = 0;
                    break;
                case CrossAxisAlignment.Center:
                    crossPos = (crossAxisSize - childCrossSize) / 2;
                    break;
                case CrossAxisAlignment.End:
                    crossPos = crossAxisSize - childCrossSize;
                    break;
                case CrossAxisAlignment.Stretch:
                    crossPos = 0;
                    // Could resize child here if needed
                    break;
                default:
                    crossPos = 0;
                    break;
            }

            // Apply position
            if (FlexDirection == LayoutDirection.Horizontal)
            {
                child.LayoutX = innerX + mainPos + child.MarginLeft;
                child.LayoutY = innerY + crossPos + child.MarginTop;
            }
            else
            {
                child.LayoutX = innerX + crossPos + child.MarginLeft;
                child.LayoutY = innerY + mainPos + child.MarginTop;
            }

            // Move to next position
            mainPos += childMainSize + Gap + spacing;

            // Recursively position this child's children
            child.PositionChildren();
        }
    }
}
