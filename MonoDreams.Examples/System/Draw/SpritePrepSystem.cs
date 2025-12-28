using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

// Prepares DrawElements for static sprites and 9-patches
[With(typeof(DrawComponent), typeof(SpriteInfo), typeof(Transform), typeof(Visible))]
public class SpritePrepSystem(World world, GraphicsDevice graphicsDevice, bool pixelPerfectRendering) : AEntitySetSystem<GameState>(world)
{
    private readonly bool _pixelPerfectRendering = pixelPerfectRendering;
    private readonly Dictionary<string, Texture2D> _ninePatchTextureCache = new();

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var transform = ref entity.Get<Transform>();
        ref readonly var spriteInfo = ref entity.Get<SpriteInfo>();
        ref readonly var drawComponent = ref entity.Get<DrawComponent>();

        if (spriteInfo.NinePatchData.HasValue && spriteInfo.SpriteSheet != null)
        {
            // Create a runtime nine-patch texture
            var ninePatchTexture = CreateNinePatchTexture(spriteInfo);

            var position = transform.WorldPosition + spriteInfo.Offset;
            drawComponent.Texture = ninePatchTexture;
            drawComponent.Position = _pixelPerfectRendering
                ? new Vector2(MathF.Round(position.X), MathF.Round(position.Y))
                : position;
            drawComponent.Rotation = transform.WorldRotation;
            drawComponent.Scale = transform.WorldScale;
            drawComponent.Size = spriteInfo.Size;
            drawComponent.SourceRectangle = null;
            drawComponent.Color = spriteInfo.Color;
            drawComponent.LayerDepth = spriteInfo.LayerDepth;
            drawComponent.Origin = spriteInfo.Origin;
        }
        // --- Handle Regular Sprite ---
        else if (spriteInfo.SpriteSheet != null)
        {
            var position = transform.WorldPosition + spriteInfo.Offset;
            drawComponent.Texture = spriteInfo.SpriteSheet;
            drawComponent.Position = _pixelPerfectRendering
                ? new Vector2(MathF.Round(position.X), MathF.Round(position.Y))
                : position;
            drawComponent.Rotation = transform.WorldRotation;
            drawComponent.Scale = transform.WorldScale;
            drawComponent.Size = spriteInfo.Size;
            drawComponent.SourceRectangle = spriteInfo.Source;
            drawComponent.Color = spriteInfo.Color;
            drawComponent.LayerDepth = spriteInfo.LayerDepth;
            drawComponent.Origin = spriteInfo.Origin;
        }
    }

    private Texture2D CreateNinePatchTexture(SpriteInfo spriteInfo)
    {
        var ninePatch = spriteInfo.NinePatchData.Value;
        var targetSize = spriteInfo.Size;
        
        // Create a cache key based on source texture, nine-patch data, and target size
        var cacheKey = $"{spriteInfo.SpriteSheet.GetHashCode()}_{ninePatch.GetHashCode()}_{targetSize.X}x{targetSize.Y}";
        
        if (_ninePatchTextureCache.TryGetValue(cacheKey, out var cachedTexture))
        {
            return cachedTexture;
        }

        // Create new render target with the target size
        var renderTarget = new RenderTarget2D(graphicsDevice, 
            (int)targetSize.X, (int)targetSize.Y, 
            false, SurfaceFormat.Color, DepthFormat.None);

        // Create a temporary SpriteBatch for rendering to the render target
        using var spriteBatch = new SpriteBatch(graphicsDevice);
        
        // Set render target and clear
        graphicsDevice.SetRenderTarget(renderTarget);
        graphicsDevice.Clear(Color.Transparent);

        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, 
            SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

        // Calculate dimensions for stretching
        var leftWidth = ninePatch.TopLeft.Width;
        var rightWidth = ninePatch.TopRight.Width;
        var topHeight = ninePatch.TopLeft.Height;
        var bottomHeight = ninePatch.BottomLeft.Height;
        
        var centerWidth = targetSize.X - leftWidth - rightWidth;
        var centerHeight = targetSize.Y - topHeight - bottomHeight;

        // Draw the 9 patches
        // Top-left corner
        spriteBatch.Draw(spriteInfo.SpriteSheet, 
            new Rectangle(0, 0, leftWidth, topHeight),
            ninePatch.TopLeft, Color.White);

        // Top edge (stretched horizontally)
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle(leftWidth, 0, (int)centerWidth, topHeight),
            ninePatch.Top, Color.White);

        // Top-right corner
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle((int)(targetSize.X - rightWidth), 0, rightWidth, topHeight),
            ninePatch.TopRight, Color.White);

        // Left edge (stretched vertically)
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle(0, topHeight, leftWidth, (int)centerHeight),
            ninePatch.Left, Color.White);

        // Center (stretched both ways)
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle(leftWidth, topHeight, (int)centerWidth, (int)centerHeight),
            ninePatch.Center, Color.White);

        // Right edge (stretched vertically)
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle((int)(targetSize.X - rightWidth), topHeight, rightWidth, (int)centerHeight),
            ninePatch.Right, Color.White);

        // Bottom-left corner
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle(0, (int)(targetSize.Y - bottomHeight), leftWidth, bottomHeight),
            ninePatch.BottomLeft, Color.White);

        // Bottom edge (stretched horizontally)
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle(leftWidth, (int)(targetSize.Y - bottomHeight), (int)centerWidth, bottomHeight),
            ninePatch.Bottom, Color.White);

        // Bottom-right corner
        spriteBatch.Draw(spriteInfo.SpriteSheet,
            new Rectangle((int)(targetSize.X - rightWidth), (int)(targetSize.Y - bottomHeight), rightWidth, bottomHeight),
            ninePatch.BottomRight, Color.White);

        spriteBatch.End();

        // Reset render target
        graphicsDevice.SetRenderTarget(null);

        // Cache the result
        _ninePatchTextureCache[cacheKey] = renderTarget;
        
        return renderTarget;
    }

    public override void Dispose()
    {
        // Clean up cached textures
        foreach (var texture in _ninePatchTextureCache.Values)
        {
            texture?.Dispose();
        }
        _ninePatchTextureCache.Clear();
    }
}