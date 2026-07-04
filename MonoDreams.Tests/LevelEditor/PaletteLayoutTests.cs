#nullable enable
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure bottom-strip palette layout (<see cref="PaletteLayout"/>): the flow grid
/// wraps item buttons into rows of the content width, scroll clamps to whole rows, scrolled-out
/// items report no rectangle (the system parks them off-screen), and the band header row lays out
/// like the toolbar's button row. World-free, like the other chrome-layout tests.
/// </summary>
public class PaletteLayoutTests
{
    private static Rectangle Strip(int w = 1600, int h = 900) =>
        EditorChromeLayout.BottomBar(w, h);

    [Fact]
    public void FlowWrapsAtContentWidth()
    {
        // Content width 300: 120 + gap(6) + 120 fits; the third 120 wraps.
        var flow = PaletteLayout.Flow(new[] { 120, 120, 120, 300, 50 }, contentWidth: 300);

        Assert.Equal((0, 0), flow[0]);
        Assert.Equal((0, 126), flow[1]);      // 120 + ButtonGap(6)
        Assert.Equal((1, 0), flow[2]);        // wrapped
        Assert.Equal((2, 0), flow[3]);        // 300 fills a row exactly (126+300 > 300 → wraps)
        Assert.Equal((3, 0), flow[4]);        // 300 + gap overflows → next row
        Assert.Equal(4, PaletteLayout.TotalRows(flow));
        Assert.Equal(0, PaletteLayout.TotalRows(PaletteLayout.Flow(new int[0], 300)));
    }

    [Fact]
    public void OverwideItemStillOccupiesARowAlone()
    {
        var flow = PaletteLayout.Flow(new[] { 500, 100 }, contentWidth: 300);
        Assert.Equal((0, 0), flow[0]); // wider than the row, placed anyway (x == 0 → no wrap loop)
        Assert.Equal((1, 0), flow[1]);
    }

    [Fact]
    public void ScrollClampsToWholeRows()
    {
        var strip = Strip();
        var visible = PaletteLayout.VisibleRowCount(strip);
        Assert.True(visible >= 1);

        Assert.Equal(0, PaletteLayout.ClampScroll(-3, totalRows: 10, strip));
        Assert.Equal(10 - visible, PaletteLayout.MaxScroll(10, strip));
        Assert.Equal(10 - visible, PaletteLayout.ClampScroll(99, totalRows: 10, strip));
        Assert.Equal(0, PaletteLayout.MaxScroll(visible, strip)); // everything fits → no scroll

        // Wheel: one notch (120) = one row; wheel-up scrolls up (negative).
        Assert.Equal(-PaletteLayout.RowsPerNotch, PaletteLayout.ScrollRows(120));
        Assert.Equal(PaletteLayout.RowsPerNotch, PaletteLayout.ScrollRows(-120));
    }

    [Fact]
    public void ItemRectVisibleAndScrolledOut()
    {
        var strip = Strip();
        var content = PaletteLayout.ContentArea(strip);
        var visible = PaletteLayout.VisibleRowCount(strip);

        // Row 0, x 0, no scroll: on-screen, under the band header, button-height tall.
        Assert.True(PaletteLayout.TryItemRect(strip, (0, 0), width: 80, scroll: 0, out var rect));
        Assert.Equal(content.X, rect.X);
        Assert.Equal(80, rect.Width);
        Assert.Equal(PaletteLayout.ButtonHeight, rect.Height);
        Assert.True(rect.Y >= content.Y + PaletteLayout.HeaderHeight - 2); // below the header row

        // Scrolled out above → no rect (the system parks the entity).
        Assert.False(PaletteLayout.TryItemRect(strip, (0, 0), 80, scroll: 1, out _));
        // Beyond the visible rows below → no rect.
        Assert.False(PaletteLayout.TryItemRect(strip, (visible, 0), 80, scroll: 0, out _));
        // The same far row scrolls INTO view.
        Assert.True(PaletteLayout.TryItemRect(strip, (visible, 0), 80, scroll: 1, out _));
    }

    [Fact]
    public void BandRowLaysOutLeftToRightInsideTheHeader()
    {
        var strip = Strip();
        var content = PaletteLayout.ContentArea(strip);
        var rects = PaletteLayout.BandRow(strip, new[] { 70, 60, 66 });

        Assert.Equal(content.X, rects[0].X);
        Assert.Equal(rects[0].Right + PaletteLayout.ButtonGap, rects[1].X);
        Assert.Equal(rects[1].Right + PaletteLayout.ButtonGap, rects[2].X);
        foreach (var r in rects)
        {
            Assert.Equal(PaletteLayout.ButtonHeight, r.Height);
            Assert.True(r.Y >= content.Y && r.Bottom <= content.Y + PaletteLayout.HeaderHeight);
        }
    }

    [Fact]
    public void StripFitsHeaderPlusThreeRows()
    {
        // The raised BottomBarHeight (104) hosts the header + ≥3 item rows — the palette's design.
        Assert.True(PaletteLayout.VisibleRowCount(Strip()) >= 3);
    }

    // ---- Thumbnails (Slice 4) ----

    [Fact]
    public void ItemButtonReservesTheThumbnailBoxAndOffsetsTheLabel()
    {
        // A sprite item's width = leading pad + thumbnail box + gap + label + trailing pad, and its
        // label starts past the thumbnail box.
        var offset = PaletteLayout.ItemLabelOffsetX();
        Assert.Equal(
            PaletteLayout.ButtonPaddingX + PaletteLayout.ThumbnailSize + PaletteLayout.ButtonGap,
            offset);
        Assert.Equal(offset + 40 + PaletteLayout.ButtonPaddingX, PaletteLayout.ItemWidth(40f));
        // The thumbnail box is a square at the left of the item rect, vertically centered.
        var rect = new Rectangle(100, 200, 120, 20);
        var box = PaletteLayout.ItemThumbnailRect(rect, 1f);
        Assert.Equal(new Rectangle(100 + PaletteLayout.ButtonPaddingX,
            200 + (20 - PaletteLayout.ThumbnailSize) / 2, PaletteLayout.ThumbnailSize,
            PaletteLayout.ThumbnailSize), box);
    }

    [Fact]
    public void ThumbnailFitPreservesAspectAndCenters()
    {
        var box = new Rectangle(10, 10, 16, 16);

        // A wide 32×16 source fits to 16×8, centered vertically in the 16×16 box.
        var wide = PaletteLayout.ThumbnailFit(box, 32, 16);
        Assert.Equal(new Rectangle(10, 14, 16, 8), wide);

        // A square source fills the box.
        Assert.Equal(box, PaletteLayout.ThumbnailFit(box, 48, 48));

        // A degenerate source collapses to an empty rect (the caller draws nothing — label fallback),
        // no crash / no divide-by-zero.
        Assert.Equal(0, PaletteLayout.ThumbnailFit(box, 0, 0).Width);
    }
}
