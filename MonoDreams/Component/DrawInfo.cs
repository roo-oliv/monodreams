using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public struct DrawInfo
{
    public Texture2D SpriteSheet;
    public Rectangle Source;
    public Rectangle Destination;
    public Color Color;

    public DrawInfo(Texture2D spriteSheet, Rectangle source, Color color, Rectangle destination)
    {
        SpriteSheet = spriteSheet;
        Source = source;
        Color = color;
        Destination = destination;
    }
    
    public DrawInfo(Texture2D texture, Color? color = null)
    {
        SpriteSheet = texture;
        Source = texture.Bounds;
        Color = color ?? Color.White;
        Destination = texture.Bounds;
    }
}