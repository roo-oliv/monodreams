namespace MonoDreams.Examples.Draw;

/// <summary>
/// Draw layer ordering for the main game screens (LDtk/Blender levels).
/// First member = 1.0 (front), last = 0.0 (back), evenly spaced.
/// </summary>
public enum GameDrawLayer
{
    Debug,          // front - debug overlays
    Cursor,         // cursor on top
    UI,             // HUD, game over text
    Effects,        // orbs, particles
    Characters,     // player, NPCs
    Foreground,     // foreground decoration
    Environment,    // walls, platforms
    Tiles,          // tile layers
    DialogueUI,     // dialogue box
    Background,     // background art
    FarBackground,  // furthest back
}
