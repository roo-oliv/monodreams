using Iguina.Defs;
using Iguina.Drivers;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Numerics;
using MonoDreams.Component;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.UI;

/// <summary>
/// Provide rendering for the GUI system.
/// </summary>
internal class IguinaRenderer : IRenderer
{
    GraphicsDevice _device;
    SpriteBatch _spriteBatch;
    ContentManager _content;
    string _assetsRoot;
    Texture2D _whiteTexture;

    Dictionary<string, BitmapFont> _fonts = new();
    Dictionary<string, Texture2D> _textures = new();

    public float GlobalTextScale = 0.5f;

    /// <summary>
    /// Create the monogame renderer.
    /// </summary>
    /// <param name="assetsPath">Root directory to load assets from. Check out the demo project for details.</param>
    public IguinaRenderer(ContentManager content, GraphicsDevice device, SpriteBatch spriteBatch, string assetsPath)
    {
        _content = content;
        _device = device;
        _spriteBatch = spriteBatch;
        _assetsRoot = assetsPath;

        // create white texture
        _whiteTexture = new Texture2D(_device, 1, 1);
        _whiteTexture.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Load / get font.
    /// </summary>
    BitmapFont GetFont(string? fontName)
    {
        var fontNameOrDefault = fontName ?? "kaph-regular-72px-white-fnt";
        if (_fonts.TryGetValue(fontNameOrDefault, out var font))
        { 
            return font;
        }

        var ret = _content.Load<BitmapFont>($"Fonts/{fontNameOrDefault}");
        ret.LetterSpacing = 5;
        _fonts[fontNameOrDefault] = ret;
        return ret;
    }

    /// <summary>
    /// Load / get texture.
    /// </summary>
    Texture2D GetTexture(string textureId)
    {
        if (_textures.TryGetValue(textureId, out var texture))
        {
            return texture;
        }

        var path = Path.Combine(_assetsRoot, textureId);
        var ret = Texture2D.FromFile(_device, path);
        _textures[textureId] = ret;
        return ret;
    }

    /// <summary>
    /// Load / get effect from id.
    /// </summary>
    Effect? GetEffect(string? effectId)
    {
        if (effectId == null) { return null; }
        return _content.Load<Effect>(effectId);
    }

    /// <summary>
    /// Set active effect id.
    /// </summary>
    void SetEffect(string? effectId)
    {
        if (_currEffectId != effectId)
        {
            _spriteBatch.End();
            _currEffectId = effectId;
            BeginBatch();
        }
    }
    string? _currEffectId;

    /// <summary>
    /// Convert iguina color to mg color.
    /// </summary>
    Microsoft.Xna.Framework.Color ToMgColor(Color color)
    {
        var colorMg = new Microsoft.Xna.Framework.Color(color.R, color.G, color.B, color.A);
        if (color.A < 255)
        {
            float factor = (float)color.A / 255f;
            colorMg.R = (byte)((float)color.R * factor);
            colorMg.G = (byte)((float)color.G * factor);
            colorMg.B = (byte)((float)color.B * factor);
        }
        return colorMg;
    }

    /// <summary>
    /// Called at the beginning of every frame.
    /// </summary>
    public void StartFrame()
    {
        _currEffectId = null;
        _currScissorRegion = null;
        BeginBatch();
    }

    /// <summary>
    /// Called at the end of every frame.
    /// </summary>
    public void EndFrame()
    {
        _spriteBatch.End();
    }

    /// <inheritdoc/>
    public Rectangle GetScreenBounds()
    {
        int screenWidth = _device.Viewport.Width;
        int screenHeight = _device.Viewport.Height;
        return new Rectangle(0, 0, screenWidth, screenHeight);
    }

    /// <inheritdoc/>
    public void DrawTexture(string? effectIdentifier, string textureId, Rectangle destRect, Rectangle sourceRect, Color color)
    {
        SetEffect(effectIdentifier);
        var texture = GetTexture(textureId);
        var colorMg = ToMgColor(color);
        _spriteBatch.Draw(texture,
            new Microsoft.Xna.Framework.Rectangle(destRect.X, destRect.Y, destRect.Width, destRect.Height),
            new Microsoft.Xna.Framework.Rectangle(sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height),
            colorMg);
    }

    /// <inheritdoc/>
    public Point MeasureText(string text, string? fontId, int fontSize, float spacing)
    {
        var bitmapFont = GetFont(fontId);
        float scale = (fontSize / 24f) * GlobalTextScale; // 24 is the default font sprite size. you need to adjust this to your own sprite font.
        // spriteFont.Spacing = spacing - 1f;
        var size = bitmapFont.MeasureString(text) * scale;
        return new Point((int)size.Width, (int)size.Height);
    }

    /// <inheritdoc/>
    public int GetTextLineHeight(string? fontId, int fontSize)
    {
        return (int)MeasureText("WI", fontId, fontSize, 1f).Y;
    }

    /// <inheritdoc/>

    [Obsolete("Note: currently we render outline in a primitive way. To improve performance and remove some visual artifact during transitions, its best to implement a shader that draw text with outline properly.")]
    public void DrawText(string? effectIdentifier, string text, string? fontId, int fontSize, Point position, Color fillColor, Color outlineColor, int outlineWidth, float spacing)
    {
        SetEffect(effectIdentifier);

        var bitmapFont = GetFont(fontId);
        float scale = (fontSize / 24f) * GlobalTextScale; // 24 is the default font sprite size. you need to adjust this to your own sprite font.

        // draw outline
        if ((outlineColor.A > 0) && (outlineWidth > 0))
        {
            // because we draw outline in a primitive way, we want it to fade a lot faster than fill color
            if (outlineColor.A < 255)
            {
                float alphaFactor = (float)(outlineColor.A / 255f);
                outlineColor.A = (byte)((float)fillColor.A * Math.Pow(alphaFactor, 7));
            }

            // draw outline
            var outline = ToMgColor(outlineColor);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X - outlineWidth, position.Y), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X, position.Y - outlineWidth), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X + outlineWidth, position.Y), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X, position.Y + outlineWidth), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X - outlineWidth, position.Y - outlineWidth), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X - outlineWidth, position.Y + outlineWidth), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X + outlineWidth, position.Y - outlineWidth), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X + outlineWidth, position.Y + outlineWidth), outline, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
        }

        // draw fill
        {
            var colorMg = ToMgColor(fillColor);
            _spriteBatch.DrawString(bitmapFont, text, new Microsoft.Xna.Framework.Vector2(position.X, position.Y), colorMg, 0f, new Microsoft.Xna.Framework.Vector2(0, 0), scale, SpriteEffects.None, 0f);
        }
    }

    /// <inheritdoc/>
    public void DrawRectangle(Rectangle rectangle, Color color)
    {
        SetEffect(null);

        var texture = _whiteTexture;
        var colorMg = ToMgColor(color);
        _spriteBatch.Draw(texture,
            new Microsoft.Xna.Framework.Rectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
            null,
            colorMg);
    }

    /// <inheritdoc/>
    public void SetScissorRegion(Rectangle region)
    {
        _currScissorRegion = region;
        _currEffectId = null;
        _spriteBatch.End();
        BeginBatch();
    }

    /// <inheritdoc/>
    public Rectangle? GetScissorRegion()
    {
        return _currScissorRegion;
    }

    // current scissor region
    Rectangle? _currScissorRegion = null;

    /// <summary>
    /// Begin a new rendering batch.
    /// </summary>
    void BeginBatch()
    {
        var effect = GetEffect(_currEffectId);
        if (_currScissorRegion != null)
        {
            _device.ScissorRectangle = new Microsoft.Xna.Framework.Rectangle(_currScissorRegion.Value.X, _currScissorRegion.Value.Y, _currScissorRegion.Value.Width, _currScissorRegion.Value.Height);
        }
        var raster = new RasterizerState();
        raster.CullMode = _device.RasterizerState.CullMode;
        raster.DepthBias = _device.RasterizerState.DepthBias;
        raster.FillMode = _device.RasterizerState.FillMode;
        raster.MultiSampleAntiAlias = _device.RasterizerState.MultiSampleAntiAlias;
        raster.SlopeScaleDepthBias = _device.RasterizerState.SlopeScaleDepthBias;
        raster.ScissorTestEnable = _currScissorRegion.HasValue;
        _device.RasterizerState = raster;
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, effect: effect, rasterizerState: raster);
    }

    /// <inheritdoc/>
    public void ClearScissorRegion()
    {
        _currScissorRegion = null;
        _currEffectId = null;
        _spriteBatch.End();
        BeginBatch();
    }

    /// <inheritdoc/>
    public Color GetPixelFromTexture(string textureId, Point sourcePosition)
    {
        var texture = GetTexture(textureId);
        var pixelData = new Microsoft.Xna.Framework.Color[1];
        if (sourcePosition.X < 0) sourcePosition.X = 0;
        if (sourcePosition.Y < 0) sourcePosition.Y = 0;
        if (sourcePosition.X >= texture.Width) sourcePosition.X = texture.Width - 1;
        if (sourcePosition.Y >= texture.Height) sourcePosition.Y = texture.Height - 1;
        texture.GetData(0, new Microsoft.Xna.Framework.Rectangle(sourcePosition.X, sourcePosition.Y, 1, 1), pixelData, 0, 1);
        var pixelColor = pixelData[0];
        return new Color(pixelColor.R, pixelColor.G, pixelColor.B, pixelColor.A);
    }

    /// <inheritdoc/>
    public Point? FindPixelOffsetInTexture(string textureId, Rectangle sourceRect, Color color, bool returnNearestColor)
    {
        var texture = GetTexture(textureId);
        var pixelData = new Microsoft.Xna.Framework.Color[sourceRect.Width * sourceRect.Height];
        texture.GetData(0, new Microsoft.Xna.Framework.Rectangle(sourceRect.X, sourceRect.Y, sourceRect.Width, sourceRect.Height), pixelData, 0, pixelData.Length);
        Point? ret = null;
        float nearestDistance = 255f * 255f * 255f * 255f;
        for (int x = 0; x < sourceRect.Width; x++)
        {
            for (int y = 0; y < sourceRect.Height; y++)
            {
                var curr = pixelData[x + y * sourceRect.Width];
                if (curr.R == color.R && curr.G == color.G && curr.B == color.B && curr.A == color.A)
                {
                    return new Point(x, y);
                }
                else if (returnNearestColor)
                {
                    float distance = Vector4.Distance(new Vector4(curr.R, curr.G, curr.B, curr.A), new Vector4(color.R, color.G, color.B, color.A));
                    if (distance <  nearestDistance)
                    {
                        nearestDistance = distance;
                        ret = new Point(x, y);
                    }
                }
            }
        }
        return ret;
    }

    /// <summary>
    /// MonoGame measure string sucks and return wrong result.
    /// So I copied the code that render string and changed it to measure instead.
    /// </summary>
    Point MeasureStringNew(SpriteFont spriteFont, string text, float scale)
    {
        var matrix = Microsoft.Xna.Framework.Matrix.Identity;
        {
            matrix.M11 = scale;
            matrix.M22 = scale;
            matrix.M41 = 0;
            matrix.M42 = 0;
        }

        // fix a bug in measuring just a single space
        if (text.Length == 1) 
        {
            var singleRet = spriteFont.MeasureString(text);
            return new Point((int)Math.Ceiling(singleRet.X * scale), (int)Math.Ceiling(singleRet.Y * scale));
        }

        bool flag3 = true;
        var zero2 = Microsoft.Xna.Framework.Vector2.Zero;
        Point ret = new Point();
        {
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\n':
                        zero2.X = 0f;
                        zero2.Y += spriteFont.LineSpacing;
                        flag3 = true;
                        continue;
                    case '\r':
                        continue;
                }

                var glyph = spriteFont.GetGlyphs()[c];
                if (flag3)
                {
                    zero2.X = Math.Max(glyph.LeftSideBearing, 0f);
                    flag3 = false;
                }
                else
                {
                    zero2.X += spriteFont.Spacing + glyph.LeftSideBearing;
                }

                Microsoft.Xna.Framework.Vector2 position2 = zero2;

                position2.X += glyph.Cropping.X;
                position2.Y += glyph.Cropping.Y;
                Microsoft.Xna.Framework.Vector2.Transform(ref position2, ref matrix, out position2);
                ret.X = (int)Math.Max((float)(position2.X + (float)glyph.BoundsInTexture.Width * scale), (float)(ret.X));
                ret.Y = (int)Math.Max((float)(position2.Y + (float)spriteFont.LineSpacing * scale), (float)(ret.Y));

                zero2.X += glyph.Width + glyph.RightSideBearing;
            }
        }

        //ret.Y += spriteFont.LineSpacing / 2;
        return ret;
    }
}