using Microsoft.Xna.Framework;

namespace MonoDreams.Demos.UI;

/// Dark-navy theme shared by every MonoDreams demo screen, plus the greyscale
/// button ramp. The demos draw entirely with generated meshes (no sprite pack),
/// so the only colors here are the screen theme, a few world accents, and the
/// button fill/outline/text states.
public static class DemoPalette
{
    // ─── screen theme ─────────────────────────────────────────────────────────
    /// Screen background — dark navy blue.
    public static readonly Color DarkBg          = new(0x26, 0x3D, 0x69);
    /// Per-row / per-snippet background — deeper navy beneath text on the bg.
    public static readonly Color DarkBgSecondary = new(0x21, 0x2B, 0x3F);
    /// Default text and outline color — soft cream.
    public static readonly Color TextLight       = new(0xFF, 0xF0, 0xCF);
    /// Hover text accent — slightly warmer cream.
    public static readonly Color TextHover        = new(0xFF, 0xEE, 0xC9);
    /// Selected / highlighted text — gold.
    public static readonly Color TextSelected     = new(0xFA, 0xDA, 0x93);
    /// Soft yellow — guide overlays such as the camera-bounds dashed rect.
    public static readonly Color SoftYellow       = new(0xF2, 0xE2, 0x7A);

    // ─── world accents ────────────────────────────────────────────────────────
    public static readonly Color Crimson = new(178, 62, 56);   // player ball / player shape
    public static readonly Color SkyBlue = new(118, 167, 200); // info / secondary highlight
    public static readonly Color Olive   = new(118, 138, 78);  // grass field / success

    // ─── greyscale button ramp ────────────────────────────────────────────────
    // State is conveyed by the FILL; outline + text stay constant. White default,
    // light grey on hover, darker grey on press/active, even-darker (but not too
    // dark) when disabled.
    public static readonly Color ButtonFill         = new(245, 246, 248); // near-white default
    public static readonly Color ButtonFillHover    = new(214, 217, 223); // light grey
    public static readonly Color ButtonFillActive   = new(168, 172, 181); // darker grey (pressed/selected)
    public static readonly Color ButtonFillDisabled = new(108, 112, 122); // even darker, still legible
    public static readonly Color ButtonOutline      = new(120, 124, 132); // medium-grey border
    public static readonly Color ButtonText         = new(34, 40, 56);    // dark text on the light fill
    public static readonly Color ButtonTextDisabled = new(58, 62, 74);    // muted text on the disabled fill
}
