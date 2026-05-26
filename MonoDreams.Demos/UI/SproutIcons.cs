using Microsoft.Xna.Framework;

namespace MonoDreams.Demos.UI;

/// Source rectangles for individual icons within the Sprout Lands `All Icons.png`
/// spritesheet. The sheet is 288x48 = three side-by-side copies of a 96x48 set
/// (cream / cream-alt / dark-brown tones, each 6 cols × 3 rows of 16x16 cells).
/// We index the FIRST tone (x=0..95) by default and let the icon tint system
/// recolor it; the dark-tone block has a different column order so it isn't
/// useful as a drop-in hover frame.
public static class SproutIcons
{
    public const int Cell = 16;

    private static Rectangle At(int col, int row) => new(col * Cell, row * Cell, Cell, Cell);

    // Row 0 — UI / system glyphs
    public static readonly Rectangle Gamepad  = At(0, 0);
    public static readonly Rectangle Screen   = At(1, 0);
    public static readonly Rectangle Skull    = At(2, 0);
    public static readonly Rectangle Gear     = At(3, 0);
    public static readonly Rectangle Question = At(4, 0);
    public static readonly Rectangle Star     = At(5, 0);

    // Row 1 — economy / status glyphs
    public static readonly Rectangle Exclamation = At(0, 1);
    public static readonly Rectangle Dollar      = At(1, 1);
    public static readonly Rectangle Cart        = At(2, 1);
    public static readonly Rectangle Podium      = At(3, 1);
    public static readonly Rectangle Trophy      = At(4, 1);
    public static readonly Rectangle Crown       = At(5, 1);

    // Row 2 — action glyphs
    public static readonly Rectangle Plus   = At(0, 2);
    public static readonly Rectangle Minus  = At(1, 2);
    public static readonly Rectangle House  = At(2, 2);   // back-to-home
    public static readonly Rectangle Check  = At(3, 2);
    public static readonly Rectangle Cross  = At(4, 2);   // close / exit
    public static readonly Rectangle Forbid = At(5, 2);
}
