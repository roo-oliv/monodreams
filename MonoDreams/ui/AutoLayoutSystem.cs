using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// System that calculates auto-layout for UI elements
/// and applies the computed positions to TransformComponent components.
/// Must run AFTER IntrinsicSizingSystem and BEFORE HierarchySystem.
/// <para>Ordinary roots share one implicit screen container and therefore STACK; a root carrying
/// <see cref="PinnedLayoutRootComponent"/> is left out of that flow and solved standalone, with
/// <see cref="PinnedLayoutRootSystem"/> (registered right after this system) placing it.</para>
/// </summary>
public class AutoLayoutSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ViewportManager _viewport;
    private readonly LayoutNodeComponent _screenRoot;
    private readonly EntitySet _slotEntities;

    public AutoLayoutSystem(World world, ViewportManager viewport)
    {
        _world = world;
        _viewport = viewport;

        // Create a screen root node that represents the AUTHORING screen — UI is authored in layout
        // units, and the per-pass camera scales those to render pixels (rendering premise
        // "Authoring space and render space are distinct"). In a single-space game layout == virtual.
        _screenRoot = new LayoutNodeComponent
        {
            Width = viewport.LayoutWidth,
            Height = viewport.LayoutHeight,
            WidthAuto = false,
            HeightAuto = false
        };

        // Query for all layout slots
        _slotEntities = world.GetEntities()
            .With<LayoutSlotComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Update screen root size in case viewport changed
        _screenRoot.Width = _viewport.LayoutWidth;
        _screenRoot.Height = _viewport.LayoutHeight;

        // Clear and rebuild the layout tree
        _screenRoot.Clear();

        // Find all root slots and build their trees
        var roots = new List<(Entity entity, ScreenAnchor anchor)>();
        var pinnedRoots = new List<Entity>();

        foreach (var entity in _slotEntities.GetEntities())
        {
            ref readonly var slot = ref entity.Get<LayoutSlotComponent>();
            if (!slot.IsRoot) continue;

            // A PINNED root is OUT OF FLOW: it is never added to the implicit screen container
            // (which stacks its children), so N pinned roots never push each other — or an
            // ordinary anchored root — around. Its own subtree is solved standalone below, and
            // PinnedLayoutRootSystem writes its final position afterwards.
            if (entity.Has<PinnedLayoutRootComponent>())
            {
                pinnedRoots.Add(entity);
                continue;
            }

            // Add this root to the screen root
            _screenRoot.AddChild(slot.Node);
            roots.Add((entity, slot.Anchor));
        }

        // Calculate the layout
        _screenRoot.CalculateLayout(_viewport.LayoutWidth, _viewport.LayoutHeight);

        // Apply layout results to transforms
        foreach (var (rootEntity, anchor) in roots)
        {
            var screenOffset = GetScreenAnchorOffset(anchor, rootEntity);
            ApplyLayout(rootEntity, screenOffset, isRoot: true);
        }

        // Solve each pinned root's subtree on its own, against the full virtual screen. Its node
        // has no parent, so CalculateLayout leaves it at local (0,0) and only its descendants get
        // positions — the placement itself belongs to PinnedLayoutRootSystem. The anchor offset is
        // still applied here so a screen that forgets to register that system degrades to a plain
        // anchored (un-offset) root instead of collapsing everything onto the origin.
        foreach (var rootEntity in pinnedRoots)
        {
            ref readonly var slot = ref rootEntity.Get<LayoutSlotComponent>();
            slot.Node.CalculateLayout(_viewport.VirtualWidth, _viewport.VirtualHeight);
            ApplyLayout(rootEntity, GetScreenAnchorOffset(slot.Anchor, rootEntity), isRoot: true);
        }
    }

    private Vector2 GetScreenAnchorOffset(ScreenAnchor anchor, Entity rootEntity)
    {
        ref readonly var slot = ref rootEntity.Get<LayoutSlotComponent>();
        return GetScreenAnchorOffset(
            _viewport, anchor, slot.ComputedWidth, slot.ComputedHeight, slot.Target);
    }

    /// <summary>
    /// Calculates the screen offset that places a root of size
    /// <paramref name="rootWidth"/> × <paramref name="rootHeight"/> at <paramref name="anchor"/>.
    /// Layout uses top-left origin with Y increasing downward.
    /// For Main/UI targets, MonoDreams uses center-origin world coordinates
    /// (the camera transform shifts them to screen coordinates at draw time).
    /// For HUD targets, no camera transform is applied — entities render with
    /// the same top-left-origin screen coordinates that the cursor uses, so we
    /// translate the layout offset accordingly.
    /// <para>Shared with <see cref="PinnedLayoutRootSystem"/>, which places pinned roots against
    /// the same anchor grid — one implementation of the anchor math for both.</para>
    /// </summary>
    public static Vector2 GetScreenAnchorOffset(
        ViewportManager viewport,
        ScreenAnchor anchor,
        float rootWidth,
        float rootHeight,
        RenderTargetID target)
    {
        var halfWidth = viewport.LayoutWidth / 2f;
        var halfHeight = viewport.LayoutHeight / 2f;

        // Calculate offset based on anchor
        // The offset converts from layout coordinates (top-left: 0,0) to MonoDreams coordinates (center: 0,0)
        var centerOriginOffset = anchor switch
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

        // HUD slots render without the camera transform, so the layout output
        // must be in top-left-origin screen coordinates (the same space cursor.VirtualPosition lives in).
        return target == RenderTargetID.HUD
            ? centerOriginOffset + new Vector2(halfWidth, halfHeight)
            : centerOriginOffset;
    }

    /// <summary>
    /// Recursively applies layout results to TransformComponent components.
    /// </summary>
    private void ApplyLayout(Entity entity, Vector2 offset, bool isRoot)
    {
        ref var slot = ref entity.Get<LayoutSlotComponent>();

        // Calculate position from layout results
        var localPos = new Vector2(slot.ComputedX, slot.ComputedY);

        // Apply to transform
        if (entity.Has<TransformComponent>())
        {
            ref var transform = ref entity.Get<TransformComponent>();
            if (isRoot)
            {
                // Root slots get the full offset applied
                transform.Position = offset + localPos;
            }
            else
            {
                // Child slots only get local position (relative to parent via TransformComponent.Parent)
                transform.Position = localPos;
            }
        }

        // Recurse to child nodes in the LayoutNodeComponent hierarchy
        foreach (var childNode in slot.Node.Children)
        {
            // Find the entity that owns this LayoutNodeComponent
            var childEntity = FindEntityByNode(childNode);
            if (childEntity.HasValue && childEntity.Value.IsAlive)
            {
                ApplyLayout(childEntity.Value, offset, isRoot: false);
            }
        }
    }

    /// <summary>
    /// Finds the entity that has a LayoutSlotComponent with the given LayoutNodeComponent.
    /// </summary>
    private Entity? FindEntityByNode(LayoutNodeComponent node)
    {
        foreach (var entity in _slotEntities.GetEntities())
        {
            ref readonly var slot = ref entity.Get<LayoutSlotComponent>();
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
