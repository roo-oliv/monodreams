#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.Extension;

namespace MonoDreams.Examples.Layout;

/// <summary>
/// Fluent builder for configuring a single layout slot.
/// </summary>
public class SlotBuilder
{
    private readonly World _world;
    private readonly RenderTargetID _renderTarget;

    private Entity? _contentEntity;
    private Func<Entity, Vector2>? _sizeMeasurer;
    private float? _fixedWidth;
    private float? _fixedHeight;
    private float _marginTop, _marginRight, _marginBottom, _marginLeft;

    internal SlotBuilder(World world, RenderTargetID renderTarget)
    {
        _world = world;
        _renderTarget = renderTarget;
    }

    /// <summary>
    /// Attaches an existing entity to this slot.
    /// The entity's Transform.Parent will be set to the slot's Transform.
    /// </summary>
    public SlotBuilder Attach(Entity entity)
    {
        _contentEntity = entity;
        return this;
    }

    /// <summary>
    /// Sets a callback to measure the content entity's size.
    /// The callback receives the content entity and returns its size as Vector2.
    /// </summary>
    public SlotBuilder MeasureWith(Func<Entity, Vector2> measurer)
    {
        _sizeMeasurer = measurer;
        return this;
    }

    /// <summary>
    /// Sets a fixed width for this slot.
    /// </summary>
    public SlotBuilder Width(float pixels)
    {
        _fixedWidth = pixels;
        return this;
    }

    /// <summary>
    /// Sets a fixed height for this slot.
    /// </summary>
    public SlotBuilder Height(float pixels)
    {
        _fixedHeight = pixels;
        return this;
    }

    /// <summary>
    /// Sets uniform margin on all sides.
    /// </summary>
    public SlotBuilder Margin(float all)
    {
        _marginTop = _marginRight = _marginBottom = _marginLeft = all;
        return this;
    }

    /// <summary>
    /// Sets vertical and horizontal margins.
    /// </summary>
    public SlotBuilder Margin(float vertical, float horizontal)
    {
        _marginTop = _marginBottom = vertical;
        _marginLeft = _marginRight = horizontal;
        return this;
    }

    /// <summary>
    /// Sets margin on each side individually.
    /// </summary>
    public SlotBuilder Margin(float top, float right, float bottom, float left)
    {
        _marginTop = top;
        _marginRight = right;
        _marginBottom = bottom;
        _marginLeft = left;
        return this;
    }

    /// <summary>
    /// Builds the slot entity with LayoutSlot and Transform components.
    /// If a content entity was attached, sets its Transform.Parent to the slot's transform.
    /// </summary>
    internal (Entity slotEntity, LayoutSlot slot) Build(Entity? parentEntity)
    {
        var slotEntity = _world.CreateEntity();
        var slotTransform = new Transform(Vector2.Zero);
        slotEntity.Set(slotTransform);

        if (parentEntity.HasValue)
        {
            slotEntity.SetParent(parentEntity.Value);
        }

        var slot = new LayoutSlot
        {
            Target = _renderTarget,
            NeedsRemeasure = _sizeMeasurer != null
        };

        // Configure margins
        slot.Node.MarginTop = _marginTop;
        slot.Node.MarginRight = _marginRight;
        slot.Node.MarginBottom = _marginBottom;
        slot.Node.MarginLeft = _marginLeft;

        // Configure fixed sizing
        if (_fixedWidth.HasValue)
        {
            slot.Node.Width = _fixedWidth.Value;
            slot.Node.WidthAuto = false;
        }

        if (_fixedHeight.HasValue)
        {
            slot.Node.Height = _fixedHeight.Value;
            slot.Node.HeightAuto = false;
        }

        // Attach content entity
        if (_contentEntity.HasValue && _contentEntity.Value.IsAlive)
        {
            slot.Content = _contentEntity;
            slot.SizeMeasurer = _sizeMeasurer;

            // Set content entity as child of slot
            _contentEntity.Value.SetParent(slotEntity);
        }

        slotEntity.Set(slot);
        return (slotEntity, slot);
    }
}
