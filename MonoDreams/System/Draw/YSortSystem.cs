using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

/// <summary>
/// Dynamically adjusts DrawComponent.LayerDepth for entities on Y-sorted layers,
/// so that entities lower on screen render in front of entities higher on screen.
/// Runs after SpritePrepSystem (which sets LayerDepth from SpriteInfo) and overwrites
/// LayerDepth with the Y-interpolated value for participating entities.
///
/// A PostUpdate pass re-derives biased children's depth from their parent's final depth,
/// ensuring parent-child pairs always sort as a group (no external entity can slip between them).
/// </summary>
[With(typeof(DrawComponent), typeof(SpriteInfo), typeof(Transform), typeof(Visible))]
public class YSortSystem(World world, MonoDreams.Component.Camera camera, DrawLayerMap drawLayerMap)
    : AEntitySetSystem<GameState>(world)
{
    private float _boundsTop;
    private float _boundsHeight;
    private readonly List<Entity> _biasedEntities = new();

    protected override void PreUpdate(GameState state)
    {
        var bounds = camera.VirtualScreenBounds;
        _boundsTop = bounds.Top;
        _boundsHeight = bounds.Height;
        _biasedEntities.Clear();
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spriteInfo = ref entity.Get<SpriteInfo>();

        if (!drawLayerMap.TryGetYSortRange(spriteInfo.LayerDepth, out var minDepth, out var maxDepth))
            return;

        ref readonly var transform = ref entity.Get<Transform>();
        ref var drawComponent = ref entity.Get<DrawComponent>();

        // Normalize Y within camera bounds: 0 = top, 1 = bottom
        float t = _boundsHeight > 0
            ? Math.Clamp((transform.WorldPosition.Y + spriteInfo.YSortOffset - _boundsTop) / _boundsHeight, 0f, 1f)
            : 0.5f;

        // Higher t (lower on screen) → higher depth → rendered in front
        drawComponent.LayerDepth = Math.Clamp(
            minDepth + t * (maxDepth - minDepth) + spriteInfo.YSortDepthBias,
            minDepth,
            maxDepth);

        if (spriteInfo.YSortDepthBias != 0f)
            _biasedEntities.Add(entity);
    }

    protected override void PostUpdate(GameState state)
    {
        if (_biasedEntities.Count == 0) return;

        var hierarchy = world.Has<EntityHierarchy>() ? world.Get<EntityHierarchy>() : null;
        if (hierarchy == null) return;

        foreach (var child in _biasedEntities)
        {
            var parent = hierarchy.GetParent(child);
            if (!parent.HasValue || !parent.Value.IsAlive) continue;
            if (!parent.Value.Has<DrawComponent>()) continue;

            ref readonly var parentDraw = ref parent.Value.Get<DrawComponent>();
            ref readonly var childSprite = ref child.Get<SpriteInfo>();
            ref var childDraw = ref child.Get<DrawComponent>();

            if (!drawLayerMap.TryGetYSortRange(childSprite.LayerDepth, out var minDepth, out var maxDepth))
                continue;

            // Preserve bias direction but use a minimal epsilon so no external
            // entity's depth can occupy the gap between parent and child
            float minimalBias = Math.Sign(childSprite.YSortDepthBias) * 1e-6f;
            childDraw.LayerDepth = Math.Clamp(
                parentDraw.LayerDepth + minimalBias,
                minDepth,
                maxDepth);
        }
    }
}
