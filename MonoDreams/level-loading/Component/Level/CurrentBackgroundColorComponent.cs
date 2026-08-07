using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Level;

/// <summary>
/// Singleton component holding the desired background clear color, set by whichever
/// level loader has one to report (the LDtk import loader in <c>level-ldtk</c> derives
/// it from the level's own background color). Absent when the loaded level declares no
/// background — use <see cref="DefaultColor"/> then.
/// </summary>
public readonly struct CurrentBackgroundColorComponent(Color color)
{
    public readonly Color Color = color;

    // Optional: Define a default color if needed elsewhere
    public static readonly Color DefaultColor = Color.CornflowerBlue;
}