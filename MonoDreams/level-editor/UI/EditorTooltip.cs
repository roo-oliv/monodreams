#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure logic for the editor's icon-button tooltip (UX2-C): the hover-delay gate, the box sizing, and
/// the cursor-anchored placement clamped to the window — world-free / GraphicsDevice-free so the
/// timing + positioning are unit-testable (like <c>EditorChromeLayout</c> / <c>CameraNav</c>). The
/// <b>display</b> (the ONE pooled box + label on the Editor target, above the dialog band) lives in
/// <c>EditorTooltipSystem</c>; this class owns only the numbers.
///
/// <para>Metrics are LOGICAL points scaled by the viewport's device-pixel ratio (the chrome's one
/// space), so the tooltip keeps its physical size on a HiDPI backbuffer — the same DPR contract the
/// rest of the shell honours.</para>
/// </summary>
public static class EditorTooltip
{
    /// <summary>Continuous hover time (seconds) before the tooltip appears — long enough not to flash
    /// on a passing cursor, short enough to feel responsive on a deliberate hover.</summary>
    public const float HoverDelaySeconds = 0.45f;

    /// <summary>Cursor→box offset (logical points): the box sits down-and-right of the pointer so it
    /// never hides the button it describes.</summary>
    public const int OffsetX = 14;

    /// <summary>See <see cref="OffsetX"/>.</summary>
    public const int OffsetY = 18;

    /// <summary>Horizontal label padding inside the box, logical points.</summary>
    public const int PaddingX = 8;

    /// <summary>Vertical label padding inside the box, logical points.</summary>
    public const int PaddingY = 5;

    /// <summary>The box outline thickness, logical points.</summary>
    public const int BorderThickness = 1;

    /// <summary>Whether a button that has been continuously hovered for
    /// <paramref name="hoverSeconds"/> should show its tooltip yet.</summary>
    public static bool ShouldShow(float hoverSeconds) => hoverSeconds >= HoverDelaySeconds;

    /// <summary>The tooltip box size in device pixels: the (already scaled) label size plus symmetric
    /// padding scaled by the device-pixel ratio.</summary>
    public static Vector2 BoxSize(float labelWidthPx, float labelHeightPx, float scale) => new(
        labelWidthPx + 2 * EditorChromeLayout.Px(PaddingX, scale),
        labelHeightPx + 2 * EditorChromeLayout.Px(PaddingY, scale));

    /// <summary>
    /// The tooltip box's top-left, in device pixels: offset down-and-right of the cursor, then clamped
    /// so the whole box stays inside the <paramref name="screenWidth"/>×<paramref name="screenHeight"/>
    /// window (a cursor near the right/bottom edge pulls the box back in). Pure.
    /// </summary>
    public static Vector2 Position(Vector2 cursor, Vector2 boxSize, int screenWidth, int screenHeight, float scale)
    {
        var x = cursor.X + EditorChromeLayout.Px(OffsetX, scale);
        var y = cursor.Y + EditorChromeLayout.Px(OffsetY, scale);
        x = MathHelper.Clamp(x, 0f, MathF.Max(0f, screenWidth - boxSize.X));
        y = MathHelper.Clamp(y, 0f, MathF.Max(0f, screenHeight - boxSize.Y));
        return new Vector2(x, y);
    }
}
