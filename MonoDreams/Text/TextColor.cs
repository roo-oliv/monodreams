using System;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace MonoDreams.Text;

public sealed class TextColor
{
    public static readonly TextColor DefaultGray = new TextColor("DefaultGray", new Color(210, 210, 210));
    public static readonly TextColor Red = new TextColor("Red", Color.Red);

    public string Name { get; }
    public Color Color { get; }

    private TextColor(string name, Color color)
    {
        Name = name;
        Color = color;
    }

    public override string ToString() => Name;

    public static implicit operator Color(TextColor colorName) => colorName.Color;

    public static TextColor Parse(string colorName)
    {
        foreach (var field in typeof(TextColor).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is TextColor tc && tc.Name == colorName)
            {
                return tc;
            }
        }

        throw new ArgumentException($"Invalid color name: {colorName}", nameof(colorName));
    }
}