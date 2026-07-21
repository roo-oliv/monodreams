using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX2-C tooltip's pure logic (<see cref="EditorTooltip"/>): the hover-delay gate, the box
/// sizing, and the cursor-anchored placement clamped to the window — including DPR-2 scaling. The
/// display (the pooled box + label on the Editor target) lives in <c>EditorTooltipSystem</c>; these are
/// the numbers.
/// </summary>
public class EditorTooltipTests
{
    [Fact]
    public void ShouldShow_GatesOnTheHoverDelay()
    {
        Assert.False(EditorTooltip.ShouldShow(0f));
        Assert.False(EditorTooltip.ShouldShow(EditorTooltip.HoverDelaySeconds - 0.01f));
        Assert.True(EditorTooltip.ShouldShow(EditorTooltip.HoverDelaySeconds));
        Assert.True(EditorTooltip.ShouldShow(EditorTooltip.HoverDelaySeconds + 1f));
    }

    [Fact]
    public void Position_OffsetsDownRightOfTheCursor_WhenThereIsRoom()
    {
        var pos = EditorTooltip.Position(new Vector2(100, 100), new Vector2(40, 20), 1600, 900, 1f);
        Assert.Equal(100 + EditorTooltip.OffsetX, pos.X);
        Assert.Equal(100 + EditorTooltip.OffsetY, pos.Y);
    }

    [Fact]
    public void Position_ClampsSoTheBoxStaysInsideTheWindow()
    {
        // Near the right/bottom edge the box is pulled back so it stays fully on screen.
        var box = new Vector2(40, 20);
        var pos = EditorTooltip.Position(new Vector2(1590, 895), box, 1600, 900, 1f);
        Assert.Equal(1600 - box.X, pos.X);
        Assert.Equal(900 - box.Y, pos.Y);
    }

    [Fact]
    public void Position_NeverGoesNegative_EvenWhenTheBoxIsWiderThanTheWindow()
    {
        var pos = EditorTooltip.Position(new Vector2(50, 50), new Vector2(2000, 20), 1600, 900, 1f);
        Assert.Equal(0f, pos.X); // clamp lower bound wins when screenWidth - boxWidth < 0
        Assert.InRange(pos.Y, 0f, 900f);
    }

    [Fact]
    public void BoxSize_AddsSymmetricPaddingAroundTheLabel()
    {
        var size = EditorTooltip.BoxSize(labelWidthPx: 120, labelHeightPx: 16, scale: 1f);
        Assert.Equal(120 + 2 * EditorTooltip.PaddingX, size.X);
        Assert.Equal(16 + 2 * EditorTooltip.PaddingY, size.Y);
    }

    [Fact]
    public void Metrics_DoubleAtDpr2()
    {
        // Offset doubles.
        var p1 = EditorTooltip.Position(new Vector2(100, 100), new Vector2(40, 20), 4000, 4000, 1f);
        var p2 = EditorTooltip.Position(new Vector2(100, 100), new Vector2(40, 20), 4000, 4000, 2f);
        Assert.Equal(100 + EditorTooltip.OffsetX, p1.X);
        Assert.Equal(100 + EditorTooltip.OffsetX * 2, p2.X);
        Assert.Equal(100 + EditorTooltip.OffsetY * 2, p2.Y);

        // Padding doubles (isolate it with a zero-size label).
        var b1 = EditorTooltip.BoxSize(0, 0, 1f);
        var b2 = EditorTooltip.BoxSize(0, 0, 2f);
        Assert.Equal(b1.X * 2, b2.X);
        Assert.Equal(b1.Y * 2, b2.Y);
    }
}
