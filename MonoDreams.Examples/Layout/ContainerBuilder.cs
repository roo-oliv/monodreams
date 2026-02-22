#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.Extension;

namespace MonoDreams.Examples.Layout;

/// <summary>
/// Fluent builder for creating UI containers with Figma-like auto layout.
/// Containers hold layout slots which position attached entities.
/// </summary>
public class ContainerBuilder
{
    private readonly World _world;
    private readonly ContainerBuilder? _parentBuilder;
    private readonly bool _isRoot;
    private readonly ScreenAnchor _anchor;
    private readonly RenderTargetID _renderTarget;

    // Layout properties with defaults
    private LayoutDirection _direction = LayoutDirection.Vertical;
    private float _gap;
    private float _paddingTop, _paddingRight, _paddingBottom, _paddingLeft;
    private MainAxisAlignment _mainAlign = MainAxisAlignment.Start;
    private CrossAxisAlignment _crossAlign = CrossAxisAlignment.Start;
    private SizingMode _widthMode = SizingMode.HugContents;
    private SizingMode _heightMode = SizingMode.HugContents;
    private float? _fixedWidth;
    private float? _fixedHeight;
    private string? _name;

    // Built entity and its components
    private Entity? _entity;
    private LayoutSlot? _layoutSlot;
    private Transform? _transform;
    private readonly List<Action<ContainerBuilder>> _pendingChildren = [];

    internal ContainerBuilder(
        World world,
        ContainerBuilder? parentBuilder,
        bool isRoot,
        ScreenAnchor anchor,
        RenderTargetID renderTarget)
    {
        _world = world;
        _parentBuilder = parentBuilder;
        _isRoot = isRoot;
        _anchor = anchor;
        _renderTarget = renderTarget;
    }

    /// <summary>
    /// Sets the layout direction (horizontal or vertical).
    /// </summary>
    public ContainerBuilder Direction(LayoutDirection direction)
    {
        _direction = direction;
        return this;
    }

    /// <summary>
    /// Sets a debug name for this container (displayed in layout visualization).
    /// </summary>
    public ContainerBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the gap (spacing) between children.
    /// </summary>
    public ContainerBuilder Gap(float gap)
    {
        _gap = gap;
        return this;
    }

    /// <summary>
    /// Sets uniform padding on all sides.
    /// </summary>
    public ContainerBuilder Padding(float all)
    {
        _paddingTop = _paddingRight = _paddingBottom = _paddingLeft = all;
        return this;
    }

    /// <summary>
    /// Sets vertical and horizontal padding.
    /// </summary>
    public ContainerBuilder Padding(float vertical, float horizontal)
    {
        _paddingTop = _paddingBottom = vertical;
        _paddingLeft = _paddingRight = horizontal;
        return this;
    }

    /// <summary>
    /// Sets padding on each side individually.
    /// </summary>
    public ContainerBuilder Padding(float top, float right, float bottom, float left)
    {
        _paddingTop = top;
        _paddingRight = right;
        _paddingBottom = bottom;
        _paddingLeft = left;
        return this;
    }

    /// <summary>
    /// Sets alignment along the main axis (direction of layout flow).
    /// </summary>
    public ContainerBuilder AlignMain(MainAxisAlignment align)
    {
        _mainAlign = align;
        return this;
    }

    /// <summary>
    /// Sets alignment along the cross axis (perpendicular to flow).
    /// </summary>
    public ContainerBuilder AlignCross(CrossAxisAlignment align)
    {
        _crossAlign = align;
        return this;
    }

    /// <summary>
    /// Sets the width sizing mode.
    /// </summary>
    public ContainerBuilder Width(SizingMode mode)
    {
        _widthMode = mode;
        _fixedWidth = null;
        return this;
    }

    /// <summary>
    /// Sets a fixed width in pixels.
    /// </summary>
    public ContainerBuilder Width(float pixels)
    {
        _widthMode = SizingMode.Fixed;
        _fixedWidth = pixels;
        return this;
    }

    /// <summary>
    /// Sets the height sizing mode.
    /// </summary>
    public ContainerBuilder Height(SizingMode mode)
    {
        _heightMode = mode;
        _fixedHeight = null;
        return this;
    }

    /// <summary>
    /// Sets a fixed height in pixels.
    /// </summary>
    public ContainerBuilder Height(float pixels)
    {
        _heightMode = SizingMode.Fixed;
        _fixedHeight = pixels;
        return this;
    }

    /// <summary>
    /// Adds a slot for attaching an entity.
    /// </summary>
    public ContainerBuilder AddSlot(Action<SlotBuilder> configure)
    {
        _pendingChildren.Add(_ =>
        {
            var slotBuilder = new SlotBuilder(_world, _renderTarget);
            configure(slotBuilder);

            var (slotEntity, slot) = slotBuilder.Build(_entity);

            // Add to layout hierarchy
            _layoutSlot?.Node.AddChild(slot.Node);
        });
        return this;
    }

    /// <summary>
    /// Adds a nested container as a child.
    /// </summary>
    public ContainerBuilder AddContainer(Action<ContainerBuilder> configure)
    {
        _pendingChildren.Add(_ =>
        {
            // Create child container builder
            var childBuilder = new ContainerBuilder(_world, this, false, ScreenAnchor.TopLeft, _renderTarget);

            // Let the caller configure it
            configure(childBuilder);

            // Build the child
            childBuilder.Build();

            // Register in layout hierarchy
            if (_layoutSlot != null && childBuilder._layoutSlot != null)
            {
                _layoutSlot.Node.AddChild(childBuilder._layoutSlot.Node);
            }

            // Set parent-child relationship
            if (_entity.HasValue && childBuilder._entity.HasValue)
            {
                childBuilder._entity.Value.SetParent(_entity.Value);
            }
        });
        return this;
    }

    /// <summary>
    /// Builds the container entity and all its children.
    /// Returns the created entity.
    /// </summary>
    public Entity Build()
    {
        // Create the container entity
        _entity = _world.CreateEntity();

        // Transform for positioning
        _transform = new Transform(Vector2.Zero);
        _entity.Value.Set(_transform);

        // Set parent reference if this is not a root
        if (_parentBuilder?._entity != null)
        {
            _entity.Value.SetParent(_parentBuilder._entity.Value);
        }

        // Create and configure the layout slot
        _layoutSlot = new LayoutSlot
        {
            IsRoot = _isRoot,
            Anchor = _anchor,
            Target = _renderTarget
        };
        ConfigureLayoutNode(_layoutSlot.Node);
        _entity.Value.Set(_layoutSlot);

        // Build all children
        foreach (var childAction in _pendingChildren)
        {
            childAction(this);
        }

        return _entity.Value;
    }

    /// <summary>
    /// Gets the built entity (available after Build() is called).
    /// </summary>
    internal Entity? Entity => _entity;

    /// <summary>
    /// Gets the layout slot (available after Build() is called).
    /// </summary>
    internal LayoutSlot? LayoutSlot => _layoutSlot;

    /// <summary>
    /// Gets the transform (available after Build() is called).
    /// </summary>
    internal Transform? Transform => _transform;

    private void ConfigureLayoutNode(FlexLayoutNode node)
    {
        // Debug name
        node.Name = _name;

        // Direction
        node.FlexDirection = _direction;

        // Gap
        node.Gap = _gap;

        // Padding
        node.PaddingTop = _paddingTop;
        node.PaddingRight = _paddingRight;
        node.PaddingBottom = _paddingBottom;
        node.PaddingLeft = _paddingLeft;

        // Main axis alignment
        node.JustifyContent = _mainAlign;

        // Cross axis alignment
        node.AlignItems = _crossAlign;

        // Width sizing
        switch (_widthMode)
        {
            case SizingMode.HugContents:
                node.WidthAuto = true;
                node.Width = null;
                break;
            case SizingMode.FillContainer:
                node.FlexGrow = 1;
                node.WidthAuto = false;
                break;
            case SizingMode.Fixed:
                node.WidthAuto = false;
                node.Width = _fixedWidth;
                break;
        }

        // Height sizing
        switch (_heightMode)
        {
            case SizingMode.HugContents:
                node.HeightAuto = true;
                node.Height = null;
                break;
            case SizingMode.FillContainer:
                node.FlexGrow = 1;
                node.HeightAuto = false;
                break;
            case SizingMode.Fixed:
                node.HeightAuto = false;
                node.Height = _fixedHeight;
                break;
        }
    }
}
