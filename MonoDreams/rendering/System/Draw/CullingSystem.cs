using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

// CullingExempt sprites manage their own visibility (streamed tile chunks — see the component): they
// are excluded from the set entirely rather than tested and skipped, so they cost nothing here.
[With(typeof(SpriteInfoComponent), typeof(TransformComponent))]
[Without(typeof(CullingExemptComponent))]
public class CullingSystem(World world, MonoDreams.Component.Camera camera) : AEntitySetSystem<GameState>(world)
{
    public bool IsEnabled { get; set; } = true;
    public static bool DebugEnabled = false;
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
                _debugEntity.Value.Set(new TransformComponent());
                _debugEntity.Value.Set(new DrawComponent { Target = RenderTargetID.HUD });
                _debugEntity.Value.Set<VisibleComponent>();
            }

            // The HUD pass is authored in layout space — DebugMargin is already in those units, and
            // the outline spans the authoring canvas (LayoutWidth/Height, == the virtual size in a
            // single-space game), so it keeps framing the view at any render resolution.
            var debugRect = new Rectangle(
                DebugMargin, DebugMargin,
                camera.LayoutWidth - DebugMargin * 2,
                camera.LayoutHeight - DebugMargin * 2);

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
        var spriteInfo = entity.Get<SpriteInfoComponent>();

        // Only cull world-space entities (Main render target)
        // UI/HUD entities use screen coordinates — camera culling doesn't apply
        if (spriteInfo.Target != RenderTargetID.Main) return;

        var transform = entity.Get<TransformComponent>();

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
            if (!entity.Has<VisibleComponent>())
            {
                entity.Set<VisibleComponent>();
            }
        }
        else
        {
            if (entity.Has<VisibleComponent>())
            {
                entity.Remove<VisibleComponent>();
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
