using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component.Level;

/// <summary>
/// Singleton component holding the desired background clear color,
/// typically derived from the loaded LDtk level's background color.
/// </summary>
public readonly struct CurrentBackgroundColorComponent(Color color)
{
    public readonly Color Color = color;

    // Optional: Define a default color if needed elsewhere
    public static readonly Color DefaultColor = Color.CornflowerBlue;
}