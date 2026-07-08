#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure geometry for the shell's slim scrollbars — the right strip's row list and the bottom
/// shelf's card grid both draw a track + a proportional thumb on their body's right edge when the
/// content overflows the visible window (and hide it when it fits). Units are the SAME whole-line /
/// whole-row units the panels scroll in (no pixel clipping — rows still park whole; see the
/// rendering premise "No scissor in the render stack"), so this helper works for either panel by
/// taking <c>total</c> / <c>visible</c> / <c>scroll</c> line-or-row counts. World-free and
/// cursor-free, unit-testable like <see cref="EditorChromeLayout"/>.
///
/// <para><b>DPR scaling.</b> Metric constants are LOGICAL points; every function takes a
/// <c>scale</c> (the viewport manager's <c>DevicePixelRatio</c>, default 1) that multiplies them
/// into screen pixels — same physical size, denser pixels on a HiDPI backbuffer.</para>
/// </summary>
public static class EditorScrollbar
{
    /// <summary>The scrollbar track/thumb width, logical points (slim — a touch wider than the 4pt
    /// splitter so the thumb is grabbable).</summary>
    public const int Width = 6;

    /// <summary>Inset from the body's right/top/bottom edges, logical points.</summary>
    public const int Margin = 2;

    /// <summary>The thumb's minimum length, logical points, so a tiny thumb (huge content) stays
    /// grabbable.</summary>
    public const int MinThumb = 18;

    private static int Px(int points, float scale) => EditorChromeLayout.Px(points, scale);

    /// <summary>Whether a scrollbar is needed — content overflows the visible window.</summary>
    public static bool NeedsScrollbar(int totalLines, int visibleLines) =>
        visibleLines > 0 && totalLines > visibleLines;

    /// <summary>The scrollbar track: a thin vertical band down the body's right edge (inset by
    /// <see cref="Margin"/>).</summary>
    public static Rectangle Track(Rectangle body, float scale = 1f)
    {
        var w = Px(Width, scale);
        var m = Px(Margin, scale);
        return new Rectangle(
            body.Right - w - m,
            body.Y + m,
            w,
            Math.Max(1, body.Height - m * 2));
    }

    /// <summary>The thumb length in pixels for the given content ratio (clamped to
    /// <see cref="MinThumb"/> .. the track height).</summary>
    public static int ThumbLength(Rectangle track, int totalLines, int visibleLines, float scale = 1f)
    {
        if (totalLines <= 0) return track.Height;
        var min = Px(MinThumb, scale);
        var h = (int)MathF.Round(track.Height * (float)visibleLines / totalLines);
        return Math.Clamp(h, Math.Min(min, track.Height), track.Height);
    }

    /// <summary>The thumb rectangle for the current <paramref name="scroll"/> offset — proportional
    /// length, positioned by the scroll fraction down the track.</summary>
    public static Rectangle Thumb(Rectangle track, int totalLines, int visibleLines, int scroll, float scale = 1f)
    {
        var h = ThumbLength(track, totalLines, visibleLines, scale);
        var max = Math.Max(1, totalLines - visibleLines);
        var travel = Math.Max(0, track.Height - h);
        var offset = travel * Math.Clamp(scroll, 0, max) / max;
        return new Rectangle(track.X, track.Y + offset, track.Width, h);
    }

    /// <summary>Inverse of <see cref="Thumb"/>: the whole-line scroll offset a thumb whose TOP sits
    /// at <paramref name="thumbTopY"/> represents, clamped to <c>[0, total-visible]</c>.</summary>
    public static int ScrollFromThumbTop(Rectangle track, int totalLines, int visibleLines,
        float thumbTopY, float scale = 1f)
    {
        var h = ThumbLength(track, totalLines, visibleLines, scale);
        var max = Math.Max(1, totalLines - visibleLines);
        var travel = track.Height - h;
        if (travel <= 0) return 0;
        var frac = (thumbTopY - track.Y) / travel;
        return Math.Clamp((int)MathF.Round(frac * max), 0, max);
    }
}
