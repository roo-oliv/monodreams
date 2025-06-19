using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(DynamicText), typeof(Position))] // Ensures entities have these + DrawComponent (from base)
public sealed class TextPrepSystem : DrawPrepSystemBase
{
     public TextPrepSystem(World world) : base(world, false) { } // Set useParallel = true if desired and safe

     protected override void Update(GameState state, in Entity entity)
     {
        ref readonly var position = ref entity.Get<Position>();
        ref readonly var text = ref entity.Get<DynamicText>(); // State updated by TextUpdateSystem
        ref var drawComponent = ref entity.Get<DrawComponent>();

        // *** Strategy: Clear drawables added by this system type ***
         // If SpritePrepSystem already cleared, this might not be needed,
         // BUT if they run in parallel or if clearing isn't global, it's safer.
         // Alternatively, tag DrawElements with their source system/type.
         // Let's assume SpritePrepSystem cleared, and this just adds. If running parallel, MUST clear carefully.
         // drawComponent.Drawables.Clear(); // Be careful with clearing strategy

        if (text.VisibleCharacterCount <= 0 || string.IsNullOrEmpty(text.TextContent) || text.Font == null)
        {
            return; // Nothing to draw
        }

        var layerDepth = text.LayerDepth;
        var visibleText = text.TextContent.Substring(0, text.VisibleCharacterCount);

        // Add a single DrawElement for the visible text (SpriteFont handles glyphs)
        drawComponent.Drawables.Add(new DrawElement
        {
             Type = DrawElementType.Text,
             Target = text.Target,
             Text = visibleText,
             Font = text.Font,
             Position = position.Current, // Or apply alignment/origin logic here
             Color = text.Color,
             LayerDepth = layerDepth,
             Size = text.Font.MeasureString(visibleText) // Store measured size if needed elsewhere
             // Add Rotation, Origin(for alignment), Scale, Effects if needed
        });

         // If using a Bitmap Font/Glyph Atlas:
         // Instead of adding one DrawElement, you would iterate through the
         // 'text.CalculatedGlyphs' (up to VisibleGlyphCount), which should contain
         // position, source rect, texture, etc. for each glyph, and add
         // a DrawElement of Type = Sprite for each one.
     }
}