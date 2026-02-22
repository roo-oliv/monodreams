using System;
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
/// </summary>
[With(typeof(DrawComponent), typeof(SpriteInfo), typeof(Transform), typeof(Visible))]
public class YSortSystem(World world, MonoDreams.Component.Camera camera, DrawLayerMap drawLayerMap)
    : AEntitySetSystem<GameState>(world)
{
    private float _boundsTop;
    private float _boundsHeight;

    protected override void PreUpdate(GameState state)
    {
        var bounds = camera.VirtualScreenBounds;
        _boundsTop = bounds.Top;
        _boundsHeight = bounds.Height;
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
            ? Math.Clamp((transform.WorldPosition.Y - _boundsTop) / _boundsHeight, 0f, 1f)
            : 0.5f;

        // Higher t (lower on screen) → higher depth → rendered in front
        drawComponent.LayerDepth = minDepth + t * (maxDepth - minDepth);
    }
}
