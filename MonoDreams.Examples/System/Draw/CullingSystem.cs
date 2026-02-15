using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(SpriteInfo), typeof(Transform))]
public class CullingSystem(World world, MonoDreams.Component.Camera camera) : AEntitySetSystem<GameState>(world)
{
    public bool IsEnabled { get; set; } = true;
    public bool DebugEnabled { get; set; } = true;
    public int DebugMargin { get; set; } = 50;

    private Entity? _debugEntity;
    private Rectangle _cullBounds;

    protected override void PreUpdate(GameState state)
    {
        // Compute the effective culling bounds for this frame
        _cullBounds = camera.VirtualScreenBounds;

        if (DebugEnabled)
        {
            // DebugMargin is in screen pixels — convert to world units for culling
            var worldMarginX = (int)Math.Min(DebugMargin / camera.Zoom, _cullBounds.Width * 0.5f - 1);
            var worldMarginY = (int)Math.Min(DebugMargin / camera.Zoom, _cullBounds.Height * 0.5f - 1);
            worldMarginX = Math.Max(0, worldMarginX);
            worldMarginY = Math.Max(0, worldMarginY);

            _cullBounds = new Rectangle(
                _cullBounds.X + worldMarginX,
                _cullBounds.Y + worldMarginY,
                _cullBounds.Width - worldMarginX * 2,
                _cullBounds.Height - worldMarginY * 2);

            // Create or update the debug outline entity
            if (_debugEntity == null || !_debugEntity.Value.IsAlive)
            {
                _debugEntity = world.CreateEntity();
                _debugEntity.Value.Set(new Transform());
                _debugEntity.Value.Set(new DrawComponent { Target = RenderTargetID.HUD });
                _debugEntity.Value.Set<Visible>();
            }

            // HUD uses Matrix.Identity — DebugMargin is already in screen pixels
            var debugRect = new Rectangle(
                DebugMargin, DebugMargin,
                camera.VirtualWidth - DebugMargin * 2,
                camera.VirtualHeight - DebugMargin * 2);

            var generator = new RectangleOutlineMeshGenerator(debugRect, 2f, Color.Lime);
            _debugEntity.Value.Get<DrawComponent>().SetMeshData(generator);
        }
        else if (_debugEntity != null && _debugEntity.Value.IsAlive)
        {
            _debugEntity.Value.Dispose();
            _debugEntity = null;
        }
    }

    protected override void Update(GameState state, in Entity entity)
    {
        var transform = entity.Get<Transform>();
        var spriteInfo = entity.Get<SpriteInfo>();

        // Calculate scale from source to destination (matches MasterRenderSystem logic)
        var scaleX = spriteInfo.Source.Width > 0 ? spriteInfo.Size.X / spriteInfo.Source.Width : 1f;
        var scaleY = spriteInfo.Source.Height > 0 ? spriteInfo.Size.Y / spriteInfo.Source.Height : 1f;

        // Entity bounds must account for origin offset to match how SpriteBatch.Draw renders:
        // visual position = worldPosition + offset - origin * scale
        var entityBounds = new Rectangle(
            (int)(transform.WorldPosition.X + spriteInfo.Offset.X - spriteInfo.Origin.X * scaleX),
            (int)(transform.WorldPosition.Y + spriteInfo.Offset.Y - spriteInfo.Origin.Y * scaleY),
            (int)spriteInfo.Size.X,
            (int)spriteInfo.Size.Y
        );

        var isVisible = _cullBounds.Intersects(entityBounds);

        if (isVisible)
        {
            if (!entity.Has<Visible>())
            {
                entity.Set<Visible>();
            }
        }
        else
        {
            if (entity.Has<Visible>())
            {
                entity.Remove<Visible>();
            }
        }
    }

    public override void Dispose()
    {
        if (_debugEntity != null && _debugEntity.Value.IsAlive)
        {
            _debugEntity.Value.Dispose();
            _debugEntity = null;
        }
        base.Dispose();
    }
}
