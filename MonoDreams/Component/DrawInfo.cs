using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public struct DrawInfo
{
    public Texture2D SpriteSheet;
    public Point Size;
    public Rectangle Source;
    public Color Color;

    public DrawInfo(Texture2D spriteSheet, Point? size = null, Rectangle? source = null, Color? color = null)
    {
        SpriteSheet = spriteSheet;
        Size = size ?? spriteSheet.Bounds.Size;
        Source = source ?? spriteSheet.Bounds;
        Color =  color ?? Color.White;
    }
}