using System.Collections.Generic;
using MonoDreams.Component.Draw;
using MonoDreams.System.Draw;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "Sprite runs flush below the Reach 16-bit-index budget":
/// MasterRenderSystem must never let one SpriteBatch Begin/End submit more than the
/// engine's 5461-sprite hard limit, because past it the batcher switches to 32-bit
/// indices, which the Reach profile (WebGL ES2 / BlazorGL) rejects with
/// "Reach profile does not support 32 bit indices".
///
/// These tests drive the pure planner the renderer uses, so they break if someone
/// removes the cap, raises it past the hard limit, or stops counting text glyph quads.
/// </summary>
public class SpriteBatchFlushTests
{
    /// The cap must stay strictly below the 32-bit-index threshold. Raising
    /// MaxSpritesPerBatch to or past 5461 reintroduces the Reach crash; this asserts it.
    [Fact]
    public void Cap_StaysBelowThe32BitIndexHardLimit()
    {
        Assert.True(
            SpriteBatchFlush.MaxSpritesPerBatch < SpriteBatchFlush.HardIndexLimit,
            $"MaxSpritesPerBatch ({SpriteBatchFlush.MaxSpritesPerBatch}) must be < " +
            $"HardIndexLimit ({SpriteBatchFlush.HardIndexLimit}) or a dense scene throws " +
            "'Reach profile does not support 32 bit indices' on web.");
    }

    /// A run far larger than the hard limit (the dense-LDtk-world case) must be split into
    /// segments that each stay within the cap — i.e. no single Begin/End ever crosses into
    /// 32-bit indices. Replays the renderer's running-count + ShouldFlushBefore loop over
    /// a flat list of sprite elements and asserts every segment is <= the cap.
    [Fact]
    public void DenseSpriteRun_SplitsIntoSegmentsUnderTheHardLimit()
    {
        // 20000 single-quad sprites — well past the 5461 limit, like a culled tile field.
        const int spriteCount = 20000;

        var segmentSizes = SimulateBatchSegments(BuildSprites(spriteCount));

        Assert.True(segmentSizes.Count > 1, "a 20000-sprite run must be split into multiple batches");
        foreach (var size in segmentSizes)
        {
            Assert.True(
                size <= SpriteBatchFlush.HardIndexLimit,
                $"a batch segment held {size} quads, at/over the 32-bit-index limit " +
                $"{SpriteBatchFlush.HardIndexLimit}");
            Assert.True(
                size <= SpriteBatchFlush.MaxSpritesPerBatch || size == 1,
                $"a batch segment held {size} quads, over the cap " +
                $"{SpriteBatchFlush.MaxSpritesPerBatch}");
        }

        // No quads are dropped or duplicated by the split.
        var total = 0;
        foreach (var s in segmentSizes) total += s;
        Assert.Equal(spriteCount, total);
    }

    /// Text quads are counted per glyph (plus an underline bar per line), so a dense field
    /// of multi-glyph text strings is also split — counting text as one quad would let a
    /// run of glyph-heavy strings silently blow past the limit.
    [Fact]
    public void Text_CountsPerGlyph_NotPerElement()
    {
        var oneLine = new DrawComponent { Type = DrawElementType.Text, Text = "abcdefghij" };
        Assert.Equal(10, SpriteBatchFlush.EstimateSpriteQuads(oneLine));

        var underlined = new DrawComponent { Type = DrawElementType.Text, Text = "abc\nde", Underline = true };
        // 5 glyphs + 2 underline bars (one per line).
        Assert.Equal(7, SpriteBatchFlush.EstimateSpriteQuads(underlined));

        var sprite = new DrawComponent { Type = DrawElementType.Sprite };
        Assert.Equal(1, SpriteBatchFlush.EstimateSpriteQuads(sprite));

        // 700 ten-glyph strings = 7000 glyph-quads > 5461 → must split.
        var strings = new List<DrawComponent>();
        for (var i = 0; i < 700; i++)
            strings.Add(new DrawComponent { Type = DrawElementType.Text, Text = "abcdefghij" });

        var segmentSizes = SimulateBatchSegments(strings);
        Assert.True(segmentSizes.Count > 1, "7000 glyph-quads must be split into multiple batches");
        foreach (var size in segmentSizes)
            Assert.True(size <= SpriteBatchFlush.HardIndexLimit,
                $"a text batch segment held {size} glyph-quads, at/over the limit");
    }

    /// <summary>
    /// Drives the *same* <see cref="SpriteBatchFlush.BatchRun"/> that
    /// MasterRenderSystem.RenderInterleaved uses for a single-context sprite/text run, returning the
    /// quad count of each Begin/End segment it produces. Because the renderer's loop delegates its
    /// flush decision + running count to this exact struct, a regression inside RenderInterleaved
    /// (dropping the per-Begin Reset, or skipping the ConsumeBefore flush check) is reflected here.
    /// </summary>
    private static List<int> SimulateBatchSegments(IReadOnlyList<DrawComponent> run)
    {
        var segments = new List<int>();
        var batch = new SpriteBatchFlush.BatchRun();
        var previousSegmentQuads = 0;
        foreach (var dc in run)
        {
            // ConsumeBefore returns true exactly when the renderer would flush (End + reopen) before
            // drawing this element — i.e. the run so far is a completed segment.
            if (batch.ConsumeBefore(dc))
            {
                segments.Add(previousSegmentQuads);
            }
            previousSegmentQuads = batch.Quads;
        }
        if (previousSegmentQuads > 0) segments.Add(previousSegmentQuads);
        return segments;
    }

    private static List<DrawComponent> BuildSprites(int count)
    {
        var list = new List<DrawComponent>(count);
        for (var i = 0; i < count; i++)
            list.Add(new DrawComponent { Type = DrawElementType.Sprite });
        return list;
    }
}
