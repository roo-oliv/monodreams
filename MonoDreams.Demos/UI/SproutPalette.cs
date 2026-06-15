using Microsoft.Xna.Framework;

namespace MonoDreams.Demos.UI;

/// Dark blue + cream palette used across every MonoDreams demo screen.
/// Cream/brown legacy colors are kept around for sprite-on-cream interactions
/// (the Sprout Lands key caps still need brown labels), but the on-screen UI is
/// driven by the Dark* / Text* colors below.
public static class SproutPalette
{
    // ─── dark palette (current) ───────────────────────────────────────────────
    /// Screen background — dark navy blue.
    public static readonly Color DarkBg          = new(0x26, 0x3D, 0x69);
    /// Per-row / per-snippet background — deeper navy that sits beneath text on the bg.
    public static readonly Color DarkBgSecondary = new(0x21, 0x2B, 0x3F);
    /// Default text and outline color — soft cream.
    public static readonly Color TextLight       = new(0xFF, 0xF0, 0xCF);
    /// Hover text accent — slightly warmer cream.
    public static readonly Color TextHover       = new(0xFF, 0xEE, 0xC9);
    /// Selected/highlighted text — gold.
    public static readonly Color TextSelected    = new(0xFA, 0xDA, 0x93);
    /// Soft yellow — used for guide overlays such as the camera-bounds dashed rect.
    public static readonly Color SoftYellow      = new(0xF2, 0xE2, 0x7A);

    // ─── legacy cream/brown (still used by sprite assets, accent reds, etc.) ───
    public static readonly Color Cream      = new(245, 232, 207);   // (legacy) window/background
    public static readonly Color Parchment  = new(225, 207, 175);   // panel fill
    public static readonly Color WarmBrown  = new(96, 64, 46);      // cap-label text (on cream key sprite)
    public static readonly Color Brown      = new(140, 92, 56);     // secondary text / borders
    public static readonly Color MutedBrown = new(166, 138, 110);   // disabled text / hint
    public static readonly Color Terracotta = new(204, 102, 64);    // hover / accent
    public static readonly Color Olive      = new(118, 138, 78);    // success / active highlight
    public static readonly Color SkyBlue    = new(118, 167, 200);   // info / secondary highlight
    public static readonly Color Crimson    = new(178, 62, 56);     // player ball
}
