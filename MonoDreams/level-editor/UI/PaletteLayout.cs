#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's <b>asset palette</b> — the bottom strip of the shell
/// (<see cref="EditorChromeLayout.BottomBar"/>), in <b>physical screen pixels</b> like the rest of
/// the chrome. The strip is: one header row of layer-band selector buttons (Ground / Detail /
/// Props / Overhead — screen-supplied), then a flow grid of palette item buttons (text labels v1)
/// that wraps left-to-right into fixed-height rows and scrolls by whole rows on the mouse wheel
/// (the systems-panel scroll model: scrolled-out rows are parked off-screen, no clipping needed).
/// World-free and cursor-free, unit-testable like <see cref="EditorChromeLayout"/> /
/// <see cref="SystemsPanelLayout"/>.
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

    /// <summary>Each palette item row's height, logical points.</summary>
    public const int RowHeight = 24;

    /// <summary>Item/band button height inside a row, logical points.</summary>
    public const int ButtonHeight = 20;

    /// <summary>Horizontal padding between the strip edge and its content, logical points.</summary>
    public const int PaddingX = 10;

    /// <summary>Vertical padding above the header row, logical points.</summary>
    public const int PaddingY = 4;

    /// <summary>Horizontal gap between buttons, logical points.</summary>
    public const int ButtonGap = 6;

    /// <summary>Horizontal label padding inside a button, logical points.</summary>
    public const int ButtonPaddingX = 6;

    /// <summary>The square art-preview thumbnail's side inside a sprite item row, logical points
    /// (Slice 4). Sits at the row's left; the label follows it. Fits inside
    /// <see cref="ButtonHeight"/>.</summary>
    public const int ThumbnailSize = 16;

    /// <summary>Rows scrolled per mouse-wheel notch (a notch = 120 wheel units).</summary>
    public const int RowsPerNotch = 1;

    private static int Px(int points, float scale) => EditorChromeLayout.Px(points, scale);

    /// <summary>The content area inside the strip rectangle (padding removed).</summary>
    public static Rectangle ContentArea(Rectangle strip, float scale = 1f) => new(
        strip.X + Px(PaddingX, scale),
        strip.Y + Px(PaddingY, scale),
        Math.Max(1, strip.Width - Px(PaddingX, scale) * 2),
        Math.Max(1, strip.Height - Px(PaddingY, scale) * 2));

    /// <summary>A button's width for a label width already measured in screen pixels.</summary>
    public static int ButtonWidth(float labelWidth, float scale = 1f) =>
        (int)MathF.Ceiling(labelWidth) + Px(ButtonPaddingX, scale) * 2;

    /// <summary>The x-offset (screen pixels, from a sprite item button's left edge) where the label
    /// starts — past the leading padding, the thumbnail box, and a gap (Slice 4). A sprite item
    /// button always reserves the thumbnail box so the flow layout is stable whether or not the
    /// texture resolves; a missing/magenta texture simply leaves the box blank.</summary>
    public static int ItemLabelOffsetX(float scale = 1f) =>
        Px(ButtonPaddingX, scale) + Px(ThumbnailSize, scale) + Px(ButtonGap, scale);

    /// <summary>A sprite item button's width for a label width already measured in screen pixels —
    /// the label offset (leading padding + thumbnail box + gap) plus the label plus trailing
    /// padding.</summary>
    public static int ItemWidth(float labelWidth, float scale = 1f) =>
        ItemLabelOffsetX(scale) + (int)MathF.Ceiling(labelWidth) + Px(ButtonPaddingX, scale);

    /// <summary>The square thumbnail box at the left of a sprite item button rectangle, vertically
    /// centered.</summary>
    public static Rectangle ItemThumbnailRect(Rectangle itemRect, float scale = 1f)
    {
        var size = Px(ThumbnailSize, scale);
        return new Rectangle(
            itemRect.X + Px(ButtonPaddingX, scale),
            itemRect.Y + (itemRect.Height - size) / 2,
            size, size);
    }

    /// <summary>
    /// The destination rectangle to draw a <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/>
    /// sprite thumbnail into <paramref name="box"/>, preserving aspect and centered (Slice 4). A
    /// degenerate source (non-positive) collapses to an empty rect at the box centre — the caller
    /// then draws nothing (fall back to the label).
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
    /// The pure flow layout: wraps items of the given widths left-to-right into rows of the
    /// content width. Returns each item's (row, x-offset) — x relative to the content's left edge.
    /// An item wider than the whole row still occupies one row alone.
    /// </summary>
    public static (int Row, int X)[] Flow(IReadOnlyList<int> itemWidths, int contentWidth, float scale = 1f)
    {
        var result = new (int Row, int X)[itemWidths.Count];
        var gap = Px(ButtonGap, scale);
        var row = 0;
        var x = 0;
        for (var i = 0; i < itemWidths.Count; i++)
        {
            if (x > 0 && x + itemWidths[i] > contentWidth)
            {
                row++;
                x = 0;
            }
            result[i] = (row, x);
            x += itemWidths[i] + gap;
        }
        return result;
    }

    /// <summary>Total flowed row count (0 for no items).</summary>
    public static int TotalRows((int Row, int X)[] flow) => flow.Length == 0 ? 0 : flow[^1].Row + 1;

    /// <summary>How many whole item rows fit under the header (never less than 1).</summary>
    public static int VisibleRowCount(Rectangle strip, float scale = 1f)
    {
        var content = ContentArea(strip, scale);
        return Math.Max(1, (content.Height - Px(HeaderHeight, scale)) / Px(RowHeight, scale));
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
    /// The on-screen rectangle of a flowed item: its (row, x) from <see cref="Flow"/>, shifted by
    /// the current whole-row <paramref name="scroll"/>. Returns false (and an empty rect) when the
    /// item's row is scrolled out — park the entity off-screen then
    /// (<see cref="SystemsPanelLayout.ParkedPosition"/>).
    /// </summary>
    public static bool TryItemRect(Rectangle strip, (int Row, int X) flowed, int width, int scroll,
        out Rectangle rect, float scale = 1f)
    {
        var visibleRow = flowed.Row - scroll;
        if (visibleRow < 0 || visibleRow >= VisibleRowCount(strip, scale))
        {
            rect = Rectangle.Empty;
            return false;
        }

        var content = ContentArea(strip, scale);
        var rowTop = content.Y + Px(HeaderHeight, scale) + visibleRow * Px(RowHeight, scale);
        rect = new Rectangle(
            content.X + flowed.X,
            rowTop + (Px(RowHeight, scale) - Px(ButtonHeight, scale)) / 2,
            width,
            Px(ButtonHeight, scale));
        return true;
    }
}
