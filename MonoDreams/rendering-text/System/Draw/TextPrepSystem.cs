using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;
using MonoDreams.Text;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.System.Draw;

[With(typeof(DynamicTextComponent), typeof(TransformComponent))] // Ensures entities have these + DrawComponent (from base)
public sealed class TextPrepSystem(World world, bool pixelPerfectRendering, TextFacePolicyRegistry facePolicies = null)
    : AEntitySetSystem<GameState>(world)
{
    private readonly bool _pixelPerfectRendering = pixelPerfectRendering;

    /// <summary>
    /// The per-face fold + missing-glyph policies this system applies (see
    /// <see cref="TextFacePolicyRegistry"/>). A screen that passes none gets a private registry with
    /// no folds — which still WARNS, once per face + character, whenever a face is about to drop a
    /// glyph. Pass ONE registry to every screen's prep system to fold consistently and to dedupe the
    /// warnings across screens.
    /// </summary>
    public TextFacePolicyRegistry FacePolicies { get; } = facePolicies ?? new TextFacePolicyRegistry();
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
        //
        // The face's FOLD runs first, on the full content: what the reveal slices and what the font
        // measures must be the same string the renderer draws (see the rendering-text premise
        // "Per-face folds run before the reveal slice and before layout"). Because TextUpdateSystem
        // measures — and CLAMPS — the reveal count in RAW characters, the count is re-expressed in
        // FOLDED characters before the slice (ScaleRevealCount), so a fold that grows the string
        // ('…' → '...') still finishes fully revealed instead of stopping short forever.
        if (!TryGetVisibleText(FacePolicies, text.Font, text.RevealingSpeed, text.VisibleCharacterCount,
                text.TextContent, out var visibleText))
        {
            // Clear any stale text on the DrawComponent so MasterRenderSystem renders nothing this frame.
            if (entity.Has<DrawComponent>())
            {
                var existing = entity.Get<DrawComponent>();
                existing.Text = null;
            }
            return;
        }

        // Loud by default: whatever the fold did NOT cover is about to vanish mid-word in the bitmap
        // draw path. Warn once per face + character (never per frame); a face whose policy opted into
        // SilentDrop skips both the warning and this scan.
        FacePolicies.WarnOnMissingGlyphs(text.Font, visibleText);

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
    /// Resolves the string <see cref="TextPrepSystem"/> renders this frame for a given FACE: the
    /// face's fold (from <paramref name="facePolicies"/>) applied to the full content, then the
    /// reveal slice — taken with the count re-expressed in FOLDED characters by
    /// <see cref="ScaleRevealCount"/>, because <c>TextUpdateSystem</c> measures and clamps it in RAW
    /// ones. Folding first — not after the slice — is what keeps the typewriter, the measured size
    /// and the drawn glyphs talking about the same string. Pure and world-free, like the overload it
    /// delegates to.
    /// </summary>
    public static bool TryGetVisibleText(TextFacePolicyRegistry facePolicies, BitmapFont font,
        float revealingSpeed, int visibleCharacterCount, string textContent, out string visibleText)
    {
        var folded = facePolicies == null ? textContent : facePolicies.Fold(font, textContent);
        return TryGetVisibleText(revealingSpeed,
            ScaleRevealCount(visibleCharacterCount, textContent, folded), folded, out visibleText);
    }

    /// <summary>
    /// Re-expresses a reveal count measured in RAW characters as a count in FOLDED characters.
    /// <c>TextUpdateSystem</c> advances <c>VisibleCharacterCount</c> against
    /// <c>TextContent.Length</c> and CLAMPS it there (flipping <c>IsRevealed</c> at the cap), while
    /// <see cref="TextPrepSystem"/> slices the FOLDED string — so a fold that grows the content
    /// (<c>'…'</c> → <c>"..."</c>: 11 characters become 13) would otherwise stop at 11 and render
    /// "carregando." for the rest of the line's life. The map is proportional and saturating: a
    /// finished raw reveal (count ≥ raw length — including the skip-reveal that assigns the raw
    /// length outright) yields the WHOLE folded string, a started one never yields zero characters,
    /// and an unchanged fold (the same instance, or the same length) is returned untouched. Pure;
    /// no font, no world.
    /// </summary>
    public static int ScaleRevealCount(int visibleCharacterCount, string rawText, string foldedText)
    {
        if (ReferenceEquals(rawText, foldedText) || visibleCharacterCount <= 0) return visibleCharacterCount;

        var rawLength = rawText?.Length ?? 0;
        var foldedLength = foldedText?.Length ?? 0;
        if (rawLength == 0 || rawLength == foldedLength) return visibleCharacterCount;
        if (visibleCharacterCount >= rawLength) return foldedLength; // the reveal finished: show it all

        // A started reveal shows at least one character, so a shrinking fold cannot blank the label.
        var scaled = (int)Math.Round(visibleCharacterCount * (double)foldedLength / rawLength,
            MidpointRounding.AwayFromZero);
        return Math.Max(1, scaled);
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
