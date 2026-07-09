using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

[With(typeof(DynamicTextComponent), typeof(TransformComponent))] // Ensures entities have these + DrawComponent (from base)
public sealed class TextPrepSystem(World world, bool pixelPerfectRendering) : AEntitySetSystem<GameState>(world)
{
    private readonly bool _pixelPerfectRendering = pixelPerfectRendering;
    // Set useParallel = true if desired and safe

     protected override void Update(GameState state, in Entity entity)
     {
        ref readonly var transform = ref entity.Get<TransformComponent>();
        ref readonly var text = ref entity.Get<DynamicTextComponent>(); // State updated by TextUpdateSystem

        // *** Strategy: Clear drawables added by this system type ***
         // If SpritePrepSystem already cleared, this might not be needed,
         // BUT if they run in parallel or if clearing isn't global, it's safer.
         // Alternatively, tag DrawElements with their source system/type.
         // Let's assume SpritePrepSystem cleared, and this just adds. If running parallel, MUST clear carefully.
         // drawComponent.Drawables.Clear(); // Be careful with clearing strategy

        // The reveal gate is scoped to REVEALING text (see the rendering-text premise "The reveal gate
        // is scoped to revealing text"): only a configured reveal (RevealingSpeed > 0) slices the
        // content by VisibleCharacterCount — the typewriter animation TextUpdateSystem advances. STATIC
        // (non-revealing) text renders its FULL content regardless of the count, so a pooled / reassigned
        // chrome label whose TextContent changed while TextUpdateSystem was Freeze-gated (the editor)
        // never truncates or blanks on a stale count. Genuinely empty/null content renders nothing.
        if (!TryGetVisibleText(text.RevealingSpeed, text.VisibleCharacterCount, text.TextContent, out var visibleText))
        {
            // Clear any stale text on the DrawComponent so MasterRenderSystem renders nothing this frame.
            if (entity.Has<DrawComponent>())
            {
                var existing = entity.Get<DrawComponent>();
                existing.Text = null;
            }
            return;
        }

        var layerDepth = text.LayerDepth;

        // Add a single DrawElement for the visible text (SpriteFont handles glyphs)
        var position = transform.WorldPosition;
        entity.Set(new DrawComponent
        {
             Type = DrawElementType.Text,
             Target = text.Target,
             Text = visibleText,
             Font = text.Font,
             Position = _pixelPerfectRendering
                 ? new Vector2(MathF.Round(position.X), MathF.Round(position.Y))
                 : position,
             Rotation = transform.WorldRotation,
             Color = text.Color,
             Underline = text.Underline,
             LayerDepth = layerDepth,
             Size = text.Font.MeasureString(visibleText), // Store measured size if needed elsewhere
             Scale = transform.WorldScale * new Vector2(text.Scale > 0 ? text.Scale : 0.5f), // Combine world scale with DynamicTextComponent scale
             LineSpacing = text.LineSpacing > 0 ? text.LineSpacing : DynamicTextComponent.DefaultLineSpacing // Multi-line leading multiplier (engine lays out '\n' lines, not the font backend)
             // Add Origin(for alignment), Effects if needed
        });

         // If using a Bitmap Font/Glyph Atlas:
         // Instead of adding one DrawElement, you would iterate through the
         // 'text.CalculatedGlyphs' (up to VisibleGlyphCount), which should contain
         // position, source rect, texture, etc. for each glyph, and add
         // a DrawElement of Type = Sprite for each one.
     }

    /// <summary>
    /// Resolves the substring <see cref="TextPrepSystem"/> renders this frame — pure and font-free, so
    /// it is unit-testable without a GraphicsDevice or a world. The reveal gate is scoped to REVEALING
    /// text: with a configured reveal (<paramref name="revealingSpeed"/> &gt; 0) the content is sliced by
    /// <paramref name="visibleCharacterCount"/> (the typewriter animation <c>TextUpdateSystem</c>
    /// advances), and a not-yet-started reveal (count ≤ 0) renders nothing; STATIC (non-revealing) text
    /// renders its FULL content regardless of the count — so a chrome label whose count is stale (its
    /// healer, <c>TextUpdateSystem</c>, is Freeze-gated in the editor) never truncates or blanks.
    /// Empty/null content renders nothing. Returns <c>false</c> (with an empty
    /// <paramref name="visibleText"/>) when nothing should render this frame.
    /// </summary>
    public static bool TryGetVisibleText(float revealingSpeed, int visibleCharacterCount,
        string textContent, out string visibleText)
    {
        if (string.IsNullOrEmpty(textContent))
        {
            visibleText = string.Empty;
            return false;
        }

        if (revealingSpeed > 0f && visibleCharacterCount <= 0)
        {
            visibleText = string.Empty; // a configured reveal that has not started yet renders nothing
            return false;
        }

        var count = revealingSpeed > 0f
            ? Math.Min(visibleCharacterCount, textContent.Length)
            : textContent.Length; // static text: the whole string, count-independent
        visibleText = textContent.Substring(0, count);
        return true;
    }
}
