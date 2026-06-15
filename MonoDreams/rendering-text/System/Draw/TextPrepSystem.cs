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

        if (text.VisibleCharacterCount <= 0 || string.IsNullOrEmpty(text.TextContent))
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
        var visibleChars = Math.Min(text.VisibleCharacterCount, text.TextContent.Length);
        var visibleText = text.TextContent[..visibleChars];

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
}
