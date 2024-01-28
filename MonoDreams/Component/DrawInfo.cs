using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public struct DrawInfo
{
    public Texture2D SpriteSheet;
    public Rectangle Source;
    public Rectangle Destination;
    public Color Color;
}