#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's <b>asset palette</b> — the bottom strip of the shell
/// (<see cref="EditorChromeLayout.BottomBar"/>), in <b>physical screen pixels</b> like the rest of
/// the chrome. The strip is: one header row of layer-band selector buttons (Ground / Detail /
/// Props / Overhead — screen-supplied), then a flow grid of fixed-size palette <b>cards</b> — a
/// sprite preview/icon on top, a text label on the bottom, plus a small band chip in the icon's
/// top-right corner (the per-asset band mark). Cards wrap left-to-right into fixed-height rows and
/// scroll by whole rows on the mouse wheel (the systems-panel scroll model: scrolled-out rows are
/// parked off-screen, no clipping needed). World-free and cursor-free, unit-testable like
/// <see cref="EditorChromeLayout"/> / <see cref="SystemsPanelLayout"/>.
///
/// <para><b>Device-pixel-ratio scaling.</b> Metric constants are LOGICAL points; every function
/// takes a <c>scale</c> (the viewport manager's <c>DevicePixelRatio</c>, default 1) that
/// multiplies them into screen pixels — same physical size, denser pixels on a HiDPI backbuffer.
/// See <see cref="EditorChromeLayout"/>.</para>
/// </summary>
public static class PaletteLayout
{
    /// <summary>The band-selector header row height, logical points.</summary>
    public const int HeaderHeight = 24;

    /// <summary>Band/header button height inside the header row, logical points.</summary>
    public const int ButtonHeight = 20;

    /// <summary>Horizontal padding between the strip edge and its content, logical points.</summary>
    public const int PaddingX = 10;

    /// <summary>Vertical padding above the header row, logical points.</summary>
    public const int PaddingY = 4;

    /// <summary>Horizontal gap between header buttons, logical points.</summary>
    public const int ButtonGap = 6;

    /// <summary>Horizontal label padding inside a header button, logical points.</summary>
    public const int ButtonPaddingX = 6;

    // ---- Card grid (asset items) ----

    /// <summary>A palette card's width, logical points.</summary>
    public const int CardWidth = 92;

    /// <summary>A palette card's height, logical points (icon on top + label on the bottom).</summary>
    public const int CardHeight = 104;

    /// <summary>Inner padding between a card's edge and its content, logical points.</summary>
    public const int CardPadding = 6;

    /// <summary>The card's top icon/preview area height, logical points (the label row + paddings
    /// take the remainder — <c>CardPadding + CardIconHeight + CardLabelGap + CardLabelHeight +
    /// CardPadding == CardHeight</c>).</summary>
    public const int CardIconHeight = 66;

    /// <summary>The gap between the icon area and the label row inside a card, logical points.</summary>
    public const int CardLabelGap = 4;

    /// <summary>The card's bottom text-label row height, logical points.</summary>
    public const int CardLabelHeight = 18;

    /// <summary>The band-chip badge width (top-right of the icon area — the per-asset band mark),
    /// logical points.</summary>
    public const int CardChipWidth = 22;

    /// <summary>The band-chip badge height, logical points.</summary>
    public const int CardChipHeight = 15;

    /// <summary>Horizontal gap between cards in the grid, logical points.</summary>
    public const int CardGapX = 8;

    /// <summary>Vertical gap between card rows in the grid, logical points.</summary>
    public const int CardGapY = 8;

    /// <summary>The usable inner width of a card (label/icon area), logical points — the label is
    /// truncated to this before rendering so it never bleeds into the neighbouring card.</summary>
    public const int CardInnerWidth = CardWidth - CardPadding * 2;

    /// <summary>Rows scrolled per mouse-wheel notch (a notch = 120 wheel units).</summary>
    public const int RowsPerNotch = 1;

    private static int Px(int points, float scale) => EditorChromeLayout.Px(points, scale);

    /// <summary>A whole card row's pitch (card height + the inter-row gap), screen pixels.</summary>
    private static int CardRowPitch(float scale) => Px(CardHeight, scale) + Px(CardGapY, scale);

    /// <summary>The content area inside the strip rectangle (padding removed).</summary>
    public static Rectangle ContentArea(Rectangle strip, float scale = 1f) => new(
        strip.X + Px(PaddingX, scale),
        strip.Y + Px(PaddingY, scale),
        Math.Max(1, strip.Width - Px(PaddingX, scale) * 2),
        Math.Max(1, strip.Height - Px(PaddingY, scale) * 2));

    /// <summary>A header (band) button's width for a label width already measured in screen pixels.</summary>
    public static int ButtonWidth(float labelWidth, float scale = 1f) =>
        (int)MathF.Ceiling(labelWidth) + Px(ButtonPaddingX, scale) * 2;

    /// <summary>
    /// The destination rectangle to draw a <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/>
    /// sprite thumbnail into <paramref name="box"/>, preserving aspect and centered. A degenerate
    /// source (non-positive) collapses to an empty rect at the box centre — the caller then draws
    /// nothing (fall back to the label).
    /// </summary>
    public static Rectangle ThumbnailFit(Rectangle box, int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || box.Width <= 0 || box.Height <= 0)
            return new Rectangle(box.Center.X, box.Center.Y, 0, 0);

        var fit = MathF.Min((float)box.Width / sourceWidth, (float)box.Height / sourceHeight);
        var w = (int)MathF.Round(sourceWidth * fit);
        var h = (int)MathF.Round(sourceHeight * fit);
        return new Rectangle(
            box.X + (box.Width - w) / 2,
            box.Y + (box.Height - h) / 2,
            w, h);
    }

    /// <summary>The band-selector buttons, laid out left-to-right in the header row.</summary>
    public static Rectangle[] BandRow(Rectangle strip, IReadOnlyList<int> bandWidths, float scale = 1f)
    {
        var content = ContentArea(strip, scale);
        var rects = new Rectangle[bandWidths.Count];
        var height = Px(ButtonHeight, scale);
        var gap = Px(ButtonGap, scale);
        var x = content.X;
        var y = content.Y + (Px(HeaderHeight, scale) - height) / 2;
        for (var i = 0; i < bandWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, y, bandWidths[i], height);
            x += bandWidths[i] + gap;
        }
        return rects;
    }

    /// <summary>
    /// The pure card-grid flow: fixed-width cards packed left-to-right into columns of the content
    /// width, wrapping into rows. Returns each card's (row, x-offset) — x relative to the content's
    /// left edge. At least one column always fits (a card narrower than the content is guaranteed).
    /// </summary>
    public static (int Row, int X)[] CardFlow(int count, int contentWidth, float scale = 1f)
    {
        var result = new (int Row, int X)[count];
        var cardW = Px(CardWidth, scale);
        var gap = Px(CardGapX, scale);
        var columns = Math.Max(1, (contentWidth + gap) / (cardW + gap));
        for (var i = 0; i < count; i++)
        {
            var col = i % columns;
            result[i] = (i / columns, col * (cardW + gap));
        }
        return result;
    }

    /// <summary>Total flowed row count (0 for no cards).</summary>
    public static int TotalRows((int Row, int X)[] flow) => flow.Length == 0 ? 0 : flow[^1].Row + 1;

    /// <summary>How many whole card rows fit under the header (never less than 1).</summary>
    public static int VisibleRowCount(Rectangle strip, float scale = 1f)
    {
        var content = ContentArea(strip, scale);
        return Math.Max(1, (content.Height - Px(HeaderHeight, scale)) / CardRowPitch(scale));
    }

    /// <summary>The maximum row-scroll offset for <paramref name="totalRows"/> flowed rows.</summary>
    public static int MaxScroll(int totalRows, Rectangle strip, float scale = 1f) =>
        Math.Max(0, totalRows - VisibleRowCount(strip, scale));

    /// <summary>Clamps a row-scroll offset into <c>[0, MaxScroll]</c>.</summary>
    public static int ClampScroll(int scroll, int totalRows, Rectangle strip, float scale = 1f) =>
        Math.Clamp(scroll, 0, MaxScroll(totalRows, strip, scale));

    /// <summary>Wheel delta → signed row-scroll delta (wheel up = negative rows = scroll up).</summary>
    public static int ScrollRows(int wheelDelta) => -wheelDelta / 120 * RowsPerNotch;

    /// <summary>
    /// The on-screen rectangle of a flowed card: its (row, x) from <see cref="CardFlow"/>, shifted
    /// by the current whole-row <paramref name="scroll"/>. Returns false (and an empty rect) when the
    /// card's row is scrolled out — park the entity off-screen then
    /// (<see cref="SystemsPanelLayout.ParkedPosition"/>).
    /// </summary>
    public static bool TryCardRect(Rectangle strip, (int Row, int X) flowed, int scroll,
        out Rectangle rect, float scale = 1f)
    {
        var visibleRow = flowed.Row - scroll;
        if (visibleRow < 0 || visibleRow >= VisibleRowCount(strip, scale))
        {
            rect = Rectangle.Empty;
            return false;
        }

        var content = ContentArea(strip, scale);
        var rowTop = content.Y + Px(HeaderHeight, scale) + visibleRow * CardRowPitch(scale);
        rect = new Rectangle(content.X + flowed.X, rowTop, Px(CardWidth, scale), Px(CardHeight, scale));
        return true;
    }

    /// <summary>The square-ish icon/preview box at the top of a card (a thumbnail is aspect-fit into
    /// it via <see cref="ThumbnailFit"/>).</summary>
    public static Rectangle CardIconRect(Rectangle card, float scale = 1f)
    {
        var pad = Px(CardPadding, scale);
        return new Rectangle(card.X + pad, card.Y + pad,
            Math.Max(1, card.Width - pad * 2), Px(CardIconHeight, scale));
    }

    /// <summary>The bottom text-label row of a card (full inner width).</summary>
    public static Rectangle CardLabelRect(Rectangle card, float scale = 1f)
    {
        var pad = Px(CardPadding, scale);
        var h = Px(CardLabelHeight, scale);
        return new Rectangle(card.X + pad, card.Bottom - pad - h,
            Math.Max(1, card.Width - pad * 2), h);
    }

    /// <summary>The band-chip badge rectangle — the per-asset band mark — in the icon area's
    /// top-right corner.</summary>
    public static Rectangle CardChipRect(Rectangle card, float scale = 1f)
    {
        var pad = Px(CardPadding, scale);
        var w = Px(CardChipWidth, scale);
        var h = Px(CardChipHeight, scale);
        return new Rectangle(card.Right - pad - w, card.Y + pad, w, h);
    }
}
