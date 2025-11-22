using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoDreams.Text;

namespace MonoDreams.System.Draw;

public sealed class DynamicTextSystem(SpriteBatch batch, World world, RenderTarget2D renderTarget)
    : AEntitySetSystem<GameState>(world.GetEntities().With((in DynamicText t) => t.RenderTarget == renderTarget).With<Transform>().AsSet(), true)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var text = ref entity.Get<DynamicText>();
        var layerDepth = DrawLayerDepth.GetLayerDepth(text.DrawLayer);
        ref var transform = ref entity.Get<Transform>();
        if (float.IsNaN(text.RevealStartTime)) text.RevealStartTime = state.TotalTime;
        var elapsedTime = state.TotalTime - text.RevealStartTime;

        var glyphs = TextProcessor.GetGlyphs(text, transform.Position);
        var revealedChars = text.IsRevealed ? glyphs.Count : 0;

        foreach (var (glyph, color) in glyphs)
        {
            if (!text.IsRevealed)
            {
                if (revealedChars / elapsedTime > text.RevealingSpeed) return;
                revealedChars++;
            }

            if (revealedChars == glyphs.Count)
            {
                text.IsRevealed = true;
            }

            // batch.Draw(glyph.FontRegion.TextureRegion, glyph.Position, color, 0.0f, Vector2.Zero, Vector2.One, SpriteEffects.None, layerDepth);
        }
    }
}