using System;
using MonoDreams.Component.Draw;

namespace MonoDreams.System.Draw;

/// <summary>
/// Pure helpers that keep a single <c>SpriteBatch</c> Begin/End run inside the
/// 16-bit-index budget that the Reach graphics profile (WebGL ES2 / BlazorGL)
/// enforces.
/// <para>
/// MonoGame's / KNI's <c>SpriteBatch</c> packs 4 vertices + 6 indices per sprite
/// into one batch. Once a single Begin/End submits more than
/// <c>SpriteBatcher.MaxBatchSize</c> (5461) sprites, the batcher grows its index
/// buffer to 32-bit indices. The Reach profile rejects 32-bit indices, throwing
/// <c>"Reach profile does not support 32 bit indices"</c> — which is exactly why a
/// dense LDtk tile world (thousands of on-screen tiles even after culling) paints
/// on desktop (HiDef) but throws on web. HiDef accepts 32-bit indices, so capping
/// the run there is merely a few extra (cheaper) flushes — the cap is applied on
/// every profile so the renderer needs no profile branch (see the rendering
/// premise "Sprite runs flush below the Reach 16-bit-index budget").
/// </para>
/// </summary>
internal static class SpriteBatchFlush
{
    /// <summary>
    /// Maximum sprite quads submitted between a <c>SpriteBatch.Begin</c> and its
    /// matching <c>End</c>. Chosen strictly below the engine's 5461 hard limit so a
    /// run never crosses into 32-bit indices; the headroom also absorbs the
    /// conservative (over-)estimate of text glyph quads.
    /// </summary>
    public const int MaxSpritesPerBatch = 4096;

    /// <summary>
    /// The engine-level hard limit above which <c>SpriteBatch</c> switches to 32-bit
    /// indices. <see cref="MaxSpritesPerBatch"/> must stay below this; the test that
    /// guards the invariant asserts it.
    /// </summary>
    public const int HardIndexLimit = 5461;

    /// <summary>
    /// Conservative upper bound on the number of sprite quads a single sprite/text
    /// draw element submits to the <c>SpriteBatch</c>:
    /// <list type="bullet">
    /// <item>Sprite (and the pre-expanded nine-patch sprites): one quad.</item>
    /// <item>Text: one quad per glyph (newlines excluded) plus, when underlined, one
    /// quad per text line. Over-counting only triggers a flush slightly earlier — it
    /// can never let a run exceed the cap.</item>
    /// </list>
    /// Mesh elements draw through <c>BasicEffect</c>, not the <c>SpriteBatch</c>, so
    /// they contribute zero quads here.
    /// </summary>
    public static int EstimateSpriteQuads(DrawComponent dc)
    {
        switch (dc.Type)
        {
            case DrawElementType.Text:
                if (string.IsNullOrEmpty(dc.Text)) return 0;
                var lines = 1;
                var glyphs = 0;
                foreach (var ch in dc.Text)
                {
                    if (ch == '\n') lines++;
                    else glyphs++;
                }
                // One quad per glyph; underline adds at most one bar quad per line.
                return glyphs + (dc.Underline ? lines : 0);

            case DrawElementType.Mesh:
                return 0;

            // Sprite and NinePatch (already expanded to a single sprite by SpritePrepSystem).
            default:
                return 1;
        }
    }

    /// <summary>
    /// Decides whether adding <paramref name="nextQuads"/> to a batch that already holds
    /// <paramref name="currentQuads"/> would exceed <see cref="MaxSpritesPerBatch"/>, i.e.
    /// whether the caller must flush (End + Begin) before drawing the next element. A
    /// single element larger than the cap (a huge text block) is still drawn after a
    /// flush — splitting one <c>DrawString</c> is the framework's, not the engine's, job;
    /// the cap leaves enough headroom under the hard limit to absorb it.
    /// </summary>
    public static bool ShouldFlushBefore(int currentQuads, int nextQuads) =>
        currentQuads > 0 && currentQuads + nextQuads > MaxSpritesPerBatch;
}
