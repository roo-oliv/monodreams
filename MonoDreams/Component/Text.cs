using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component;

public struct Text
{
    private string _value;
    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            Origin = new Vector2(
                x: HorizontalAlign switch
                {
                    HorizontalAlign.Left => 0,
                    HorizontalAlign.Center => Font.MeasureString(value).Width / 2,
                    HorizontalAlign.Right => Font.MeasureString(value).Width,
                    _ => throw new ArgumentOutOfRangeException(),
                },
                y: VerticalAlign switch
                {
                    VerticalAlign.Top => Font.MeasureString(value).Height,
                    VerticalAlign.Center => Font.MeasureString(value).Height / 2,
                    VerticalAlign.Bottom => 0,
                    _ => throw new ArgumentOutOfRangeException(),
                });
        }
    }

    public BitmapFont Font;
    public Color Color;
    public HorizontalAlign HorizontalAlign;
    public VerticalAlign VerticalAlign;

    public Text(string value, BitmapFont font, Color color, HorizontalAlign horizontalAlign, VerticalAlign verticalAlign) : this()
    {
        Font = font;
        Color = color;
        HorizontalAlign = horizontalAlign;
        VerticalAlign = verticalAlign;
        Value = value;
    }

    public Vector2 Origin { get; private set; }
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
