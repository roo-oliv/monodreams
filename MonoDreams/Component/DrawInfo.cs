using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public struct DrawInfo
{
    public Texture2D SpriteSheet;
    public Rectangle Source;
    public Color Color;

    public DrawInfo(Texture2D spriteSheet, Rectangle source, Color color)
    {
        SpriteSheet = spriteSheet;
        Source = source;
        Color = color;
    }
    
    public DrawInfo(Texture2D texture, Color? color = null)
    {
        SpriteSheet = texture;
        Source = texture.Bounds;
        Color = color ?? Color.White;
    }
}