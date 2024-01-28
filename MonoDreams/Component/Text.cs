using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public struct Text
{
    public string Value;
    public SpriteFont SpriteFont;
    public Color Color;
    public TextAlign TextAlign;
}

public enum TextAlign
{
    Left,
    Center,
    Right
}
