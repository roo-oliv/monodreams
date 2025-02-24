using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Util;

namespace MonoDreams.Component;

public readonly struct DrawInfo
{
    public readonly RenderTarget2D RenderTarget;
    public readonly Texture2D SpriteSheet;
    public readonly Point Size;
    public readonly Rectangle Source;
    public readonly Color Color;
    public readonly Enum Layer;
    public readonly NinePatchInfo? NinePatchInfo;
    public readonly (
        Texture2D topLeft, Texture2D top, Texture2D topRight, Texture2D left, Texture2D center,
        Texture2D right, Texture2D bottomLeft, Texture2D bottom, Texture2D bottomRight) NinePatchTextures;
    public readonly (
        Rectangle topLeft, Rectangle top, Rectangle topRight, Rectangle left, Rectangle center,
        Rectangle right, Rectangle bottomLeft, Rectangle bottom, Rectangle bottomRight) NinePatchDestinations;

    public DrawInfo(
        RenderTarget2D renderTarget, Texture2D spriteSheet, Point? size = null, Rectangle? source = null,
        Color? color = null, Enum layer = null, NinePatchInfo? ninePatchInfo = null,
        GraphicsDevice? graphicsDevice = null)
    {
        RenderTarget = renderTarget;
        SpriteSheet = spriteSheet;
        Size = size ?? spriteSheet.Bounds.Size;
        Source = source ?? spriteSheet.Bounds;
        Color = color ?? Color.White;
        Layer = layer;
        NinePatchInfo = ninePatchInfo;

        if (ninePatchInfo == null || graphicsDevice == null)
        {
            NinePatchTextures = (null, null, null, null, null, null, null, null, null);
            NinePatchDestinations = (Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);
        }
        else
        {
            NinePatchTextures = (
                spriteSheet.Crop(ninePatchInfo.Value.TopLeft, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.Top, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.TopRight, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.Left, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.Center, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.Right, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.BottomLeft, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.Bottom, graphicsDevice),
                spriteSheet.Crop(ninePatchInfo.Value.BottomRight, graphicsDevice)
            );

            var borderSize = ninePatchInfo.Value.BorderSize;
            var sizeValue = Size;

            NinePatchDestinations = (
                new Rectangle(0, 0, borderSize, borderSize),
                new Rectangle(borderSize, 0, sizeValue.X - 2 * borderSize, borderSize),
                new Rectangle(sizeValue.X - borderSize, 0, borderSize, borderSize),
                new Rectangle(0, borderSize, borderSize, sizeValue.Y - 2 * borderSize),
                new Rectangle(borderSize, borderSize, sizeValue.X - 2 * borderSize, sizeValue.Y - 2 * borderSize),
                new Rectangle(sizeValue.X - borderSize, borderSize, borderSize, sizeValue.Y - 2 * borderSize),
                new Rectangle(0, sizeValue.Y - borderSize, borderSize, borderSize),
                new Rectangle(borderSize, sizeValue.Y - borderSize, sizeValue.X - 2 * borderSize, borderSize),
                new Rectangle(sizeValue.X - borderSize, sizeValue.Y - borderSize, borderSize, borderSize)
            );
        }
    }
}

public readonly struct NinePatchInfo(
    int borderSize,
    Rectangle topLeft,
    Rectangle top,
    Rectangle topRight,
    Rectangle left,
    Rectangle center,
    Rectangle right,
    Rectangle bottomLeft,
    Rectangle bottom,
    Rectangle bottomRight)
{
    public int BorderSize { get; } = borderSize;
    public Rectangle TopLeft { get; } = topLeft;
    public Rectangle Top { get; } = top;
    public Rectangle TopRight { get; } = topRight;
    public Rectangle Left { get; } = left;
    public Rectangle Center { get; } = center;
    public Rectangle Right { get; } = right;
    public Rectangle BottomLeft { get; } = bottomLeft;
    public Rectangle Bottom { get; } = bottom;
    public Rectangle BottomRight { get; } = bottomRight;
}