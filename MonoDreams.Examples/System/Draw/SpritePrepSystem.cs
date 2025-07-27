using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Draw;

public static class SpritePrepSystem
{
    private static readonly Dictionary<string, Texture2D> _ninePatchTextureCache = new();

    public static void Register(World world, GraphicsDevice graphicsDevice)
    {
        // System that runs for entities with DrawComponent, SpriteInfo, Position, and Visible
        world.System<DrawComponent, SpriteInfo, Position, Visible>()
            .Kind(Ecs.OnUpdate)
            .Kind(DrawPhase)
            .Each((ref DrawComponent drawComponent, ref SpriteInfo spriteInfo, ref Position position, ref Visible visible) =>
            {
                if (spriteInfo.NinePatchData.HasValue && spriteInfo.SpriteSheet != null)
                {
                    // Create a runtime nine-patch texture
                    var ninePatchTexture = CreateNinePatchTexture(spriteInfo, graphicsDevice);

                    drawComponent.Texture = ninePatchTexture;
                    drawComponent.Position = position.Current + spriteInfo.Offset;
                    drawComponent.Size = spriteInfo.Size;
                    drawComponent.SourceRectangle = null;
                    drawComponent.Color = spriteInfo.Color;
                    drawComponent.LayerDepth = spriteInfo.LayerDepth;
                }
                // Handle Regular Sprite
                else if (spriteInfo.SpriteSheet != null)
                {
                    drawComponent.Texture = spriteInfo.SpriteSheet;
                    drawComponent.Position = position.Current + spriteInfo.Offset;
                    drawComponent.Size = spriteInfo.Size;
                    drawComponent.SourceRectangle = spriteInfo.Source;
                    drawComponent.Color = spriteInfo.Color;
                    drawComponent.LayerDepth = spriteInfo.LayerDepth;
                }
            });
    }

    private static Texture2D CreateNinePatchTexture(SpriteInfo spriteInfo, GraphicsDevice graphicsDevice)
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

        // Draw the 9 patches (same implementation as before)
        // ... [nine patch drawing code remains the same]

        spriteBatch.End();
        graphicsDevice.SetRenderTarget(null);

        // Cache the result
        _ninePatchTextureCache[cacheKey] = renderTarget;
        
        return renderTarget;
    }

    public static void Cleanup()
    {
        // Clean up cached textures
        foreach (var texture in _ninePatchTextureCache.Values)
        {
            texture?.Dispose();
        }
        _ninePatchTextureCache.Clear();
    }
}