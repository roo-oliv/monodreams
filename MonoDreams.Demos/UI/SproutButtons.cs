using Microsoft.Xna.Framework;

namespace MonoDreams.Demos.UI;

/// Source rectangles for the Sprout Lands `Square Buttons 26x26.png` sheet
/// (96x192, 2 cols × 4 rows of 48x48 cells). Each cell contains a 26x26
/// rounded button graphic with a soft drop shadow on the bottom-right.
public static class SproutSquareButtons
{
    public const int Cell = 48;
    private static Rectangle At(int col, int row) => new(col * Cell, row * Cell, Cell, Cell);

    // Row 0 — light grey
    public static readonly Rectangle GreyLight = At(0, 0);
    public static readonly Rectangle GreyDark  = At(1, 0);
    // Row 1 — cream
    public static readonly Rectangle CreamLight = At(0, 1);
    public static readonly Rectangle CreamDark  = At(1, 1);
    // Row 2 — tan / brown
    public static readonly Rectangle TanLight  = At(0, 2);
    public static readonly Rectangle TanDark   = At(1, 2);
    // Row 3 — dark brown
    public static readonly Rectangle BrownLight = At(0, 3);
    public static readonly Rectangle BrownDark  = At(1, 3);
}

/// Source rectangles for the Sprout Lands `UI Settings Buttons.png` sheet
/// (128x240). Only the two horizontal toggle-switch frames are mapped; the
/// other glyphs in the sheet are unused by the current demos.
public static class SproutSettings
{
    /// Pill with knob on LEFT, brown body — typical "off" state.
    public static readonly Rectangle ToggleOff = new(2, 150, 28, 18);
    /// Pill with knob on RIGHT, olive body — typical "on" state.
    /// Source rect aligns to the green body's left edge; previous value started
    /// past the body and only captured the knob.
    public static readonly Rectangle ToggleOn  = new(68, 150, 30, 18);
}
