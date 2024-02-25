using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component;

public struct Text
{
    public string Value;
    public BitmapFont Font;
    public Color Color;
    public HorizontalAlign HorizontalAlign;
    public VerticalAlign VerticalAlign;
}

public enum HorizontalAlign
{
    Left,
    Center,
    Right,
}

public enum VerticalAlign
{
    Top,
    Center,
    Bottom,
}
