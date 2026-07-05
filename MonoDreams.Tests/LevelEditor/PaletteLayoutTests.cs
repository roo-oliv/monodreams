#nullable enable
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure bottom-strip palette layout (<see cref="PaletteLayout"/>): the card grid packs
/// fixed-width cards (icon on top, label on the bottom, a band chip in the icon corner) into rows of
/// the content width, scroll clamps to whole card rows, scrolled-out cards report no rectangle (the
/// system parks them off-screen), and the band header row lays out like the toolbar's button row.
/// World-free, like the other chrome-layout tests.
/// </summary>
public class PaletteLayoutTests
{
    private static Rectangle Strip(int w = 1600, int h = 900) =>
        EditorChromeLayout.BottomBar(w, h);

    [Fact]
    public void CardGridWrapsAtContentWidth()
    {
        // Content width 300: (300+8)/(92+8) = 3 columns; the 4th card wraps to row 1.
        var flow = PaletteLayout.CardFlow(5, contentWidth: 300);

        Assert.Equal((0, 0), flow[0]);
        Assert.Equal((0, 100), flow[1]);   // CardWidth(92) + CardGapX(8)
        Assert.Equal((0, 200), flow[2]);
        Assert.Equal((1, 0), flow[3]);     // wrapped
        Assert.Equal((1, 100), flow[4]);
        Assert.Equal(2, PaletteLayout.TotalRows(flow));
        Assert.Equal(0, PaletteLayout.TotalRows(PaletteLayout.CardFlow(0, 300)));
    }

    [Fact]
    public void CardGridAlwaysFitsAtLeastOneColumn()
    {
        // A content narrower than one card still packs one card per row (never zero columns).
        var flow = PaletteLayout.CardFlow(3, contentWidth: 10);
        Assert.Equal((0, 0), flow[0]);
        Assert.Equal((1, 0), flow[1]);
        Assert.Equal((2, 0), flow[2]);
    }

    [Fact]
    public void ScrollClampsToWholeCardRows()
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
    public void CardRectVisibleAndScrolledOut()
    {
        var strip = Strip();
        var content = PaletteLayout.ContentArea(strip);
        var visible = PaletteLayout.VisibleRowCount(strip);

        // Row 0, x 0, no scroll: on-screen, under the band header, card-sized.
        Assert.True(PaletteLayout.TryCardRect(strip, (0, 0), scroll: 0, out var rect));
        Assert.Equal(content.X, rect.X);
        Assert.Equal(PaletteLayout.CardWidth, rect.Width);
        Assert.Equal(PaletteLayout.CardHeight, rect.Height);
        Assert.True(rect.Y >= content.Y + PaletteLayout.HeaderHeight - 2); // below the header row

        // Scrolled out above → no rect (the system parks the entity).
        Assert.False(PaletteLayout.TryCardRect(strip, (0, 0), scroll: 1, out _));
        // Beyond the visible rows below → no rect.
        Assert.False(PaletteLayout.TryCardRect(strip, (visible, 0), scroll: 0, out _));
        // The same far row scrolls INTO view.
        Assert.True(PaletteLayout.TryCardRect(strip, (visible, 0), scroll: 1, out _));
    }

    [Fact]
    public void CardSubRects_IconTopLabelBottomChipCorner()
    {
        var card = new Rectangle(100, 200, PaletteLayout.CardWidth, PaletteLayout.CardHeight);
        var pad = PaletteLayout.CardPadding;

        // Icon box sits at the top, inset by the padding, CardIconHeight tall.
        var icon = PaletteLayout.CardIconRect(card, 1f);
        Assert.Equal(new Rectangle(100 + pad, 200 + pad,
            PaletteLayout.CardWidth - pad * 2, PaletteLayout.CardIconHeight), icon);

        // Label row sits at the bottom, full inner width, CardLabelHeight tall.
        var label = PaletteLayout.CardLabelRect(card, 1f);
        Assert.Equal(PaletteLayout.CardLabelHeight, label.Height);
        Assert.Equal(card.Bottom - pad, label.Bottom);
        Assert.Equal(100 + pad, label.X);
        Assert.True(label.Y > icon.Bottom); // strictly below the icon

        // Band chip badge sits in the icon's top-right corner.
        var chip = PaletteLayout.CardChipRect(card, 1f);
        Assert.Equal(new Rectangle(card.Right - pad - PaletteLayout.CardChipWidth, 200 + pad,
            PaletteLayout.CardChipWidth, PaletteLayout.CardChipHeight), chip);
    }

    [Fact]
    public void RaisedStripFitsHeaderPlusACardRow()
    {
        // The raised BottomBarHeight (168, up from the v1 flat-row 104) hosts the band header + at
        // least one full card row — the redesigned palette's bigger cards.
        Assert.Equal(168, EditorChromeLayout.BottomBarHeight);
        var strip = Strip();
        Assert.True(PaletteLayout.VisibleRowCount(strip) >= 1);
        // The header + one card row fit inside the content area.
        var content = PaletteLayout.ContentArea(strip);
        Assert.True(content.Height >= PaletteLayout.HeaderHeight + PaletteLayout.CardHeight);
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

    // ---- Thumbnail aspect-fit (icon box) ----

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

    [Fact]
    public void CardMetrics_AtDpr2_Double()
    {
        // DPR scaling: a card's screen rect doubles at scale 2 (physical size preserved, denser px).
        var strip = EditorChromeLayout.BottomBar(3200, 1800, 2f);
        Assert.True(PaletteLayout.TryCardRect(strip, (0, 0), scroll: 0, out var rect, 2f));
        Assert.Equal(PaletteLayout.CardWidth * 2, rect.Width);
        Assert.Equal(PaletteLayout.CardHeight * 2, rect.Height);
    }
}
