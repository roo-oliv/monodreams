using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.Examples.Layout;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Layout;

/// <summary>
/// System that calculates auto-layout for UI elements
/// and applies the computed positions to Transform components.
/// Must run AFTER IntrinsicSizingSystem and BEFORE TransformHierarchySystem.
/// </summary>
public class AutoLayoutSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ViewportManager _viewport;
    private readonly FlexLayoutNode _screenRoot;
    private readonly EntitySet _slotEntities;

    public AutoLayoutSystem(World world, ViewportManager viewport)
    {
        _world = world;
        _viewport = viewport;

        // Create a screen root node that represents the virtual screen
        _screenRoot = new FlexLayoutNode
        {
            Width = viewport.VirtualWidth,
            Height = viewport.VirtualHeight,
            WidthAuto = false,
            HeightAuto = false
        };

        // Query for all layout slots
        _slotEntities = world.GetEntities()
            .With<LayoutSlot>()
            .With<Transform>()
            .AsSet();
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Update screen root size in case viewport changed
        _screenRoot.Width = _viewport.VirtualWidth;
        _screenRoot.Height = _viewport.VirtualHeight;

        // Clear and rebuild the layout tree
        _screenRoot.Clear();

        // Find all root slots and build their trees
        var roots = new List<(Entity entity, ScreenAnchor anchor)>();

        foreach (var entity in _slotEntities.GetEntities())
        {
            ref readonly var slot = ref entity.Get<LayoutSlot>();
            if (!slot.IsRoot) continue;

            // Add this root to the screen root
            _screenRoot.AddChild(slot.Node);
            roots.Add((entity, slot.Anchor));
        }

        // Calculate the layout
        _screenRoot.CalculateLayout(_viewport.VirtualWidth, _viewport.VirtualHeight);

        // Apply layout results to transforms
        foreach (var (rootEntity, anchor) in roots)
        {
            var screenOffset = GetScreenAnchorOffset(anchor, rootEntity);
            ApplyLayout(rootEntity, screenOffset, isRoot: true);
        }
    }

    /// <summary>
    /// Calculates the screen offset based on the anchor position.
    /// Layout uses top-left origin with Y increasing downward.
    /// MonoDreams uses center origin (0,0 at screen center).
    /// </summary>
    private Vector2 GetScreenAnchorOffset(ScreenAnchor anchor, Entity rootEntity)
    {
        var halfWidth = _viewport.VirtualWidth / 2f;
        var halfHeight = _viewport.VirtualHeight / 2f;

        // Get the root's computed size to center it properly
        ref var slot = ref rootEntity.Get<LayoutSlot>();
        var rootWidth = slot.ComputedWidth;
        var rootHeight = slot.ComputedHeight;

        // Calculate offset based on anchor
        // The offset converts from layout coordinates (top-left: 0,0) to MonoDreams coordinates (center: 0,0)
        return anchor switch
        {
            // Top row
            ScreenAnchor.TopLeft => new Vector2(-halfWidth, -halfHeight),
            ScreenAnchor.TopCenter => new Vector2(-rootWidth / 2, -halfHeight),
            ScreenAnchor.TopRight => new Vector2(halfWidth - rootWidth, -halfHeight),

            // Middle row
            ScreenAnchor.CenterLeft => new Vector2(-halfWidth, -rootHeight / 2),
            ScreenAnchor.Center => new Vector2(-rootWidth / 2, -rootHeight / 2),
            ScreenAnchor.CenterRight => new Vector2(halfWidth - rootWidth, -rootHeight / 2),

            // Bottom row
            ScreenAnchor.BottomLeft => new Vector2(-halfWidth, halfHeight - rootHeight),
            ScreenAnchor.BottomCenter => new Vector2(-rootWidth / 2, halfHeight - rootHeight),
            ScreenAnchor.BottomRight => new Vector2(halfWidth - rootWidth, halfHeight - rootHeight),

            // Stretch fills the entire screen
            ScreenAnchor.Stretch => new Vector2(-halfWidth, -halfHeight),

            _ => Vector2.Zero
        };
    }

    /// <summary>
    /// Recursively applies layout results to Transform components.
    /// </summary>
    private void ApplyLayout(Entity entity, Vector2 offset, bool isRoot)
    {
        ref var slot = ref entity.Get<LayoutSlot>();

        // Calculate position from layout results
        var localPos = new Vector2(slot.ComputedX, slot.ComputedY);

        // Apply to transform
        if (entity.Has<Transform>())
        {
            ref var transform = ref entity.Get<Transform>();
            if (isRoot)
            {
                // Root slots get the full offset applied
                transform.Position = offset + localPos;
            }
            else
            {
                // Child slots only get local position (relative to parent via Transform.Parent)
                transform.Position = localPos;
            }
        }

        // Recurse to child nodes in the FlexLayoutNode hierarchy
        foreach (var childNode in slot.Node.Children)
        {
            // Find the entity that owns this FlexLayoutNode
            var childEntity = FindEntityByNode(childNode);
            if (childEntity.HasValue && childEntity.Value.IsAlive)
            {
                ApplyLayout(childEntity.Value, offset, isRoot: false);
            }
        }
    }

    /// <summary>
    /// Finds the entity that has a LayoutSlot with the given FlexLayoutNode.
    /// </summary>
    private Entity? FindEntityByNode(FlexLayoutNode node)
    {
        foreach (var entity in _slotEntities.GetEntities())
        {
            ref readonly var slot = ref entity.Get<LayoutSlot>();
            if (slot.Node == node)
            {
                return entity;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _slotEntities.Dispose();
        _screenRoot.Clear();
    }
}
