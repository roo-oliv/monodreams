using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoDreams.Text;
using MonoGame.Extended.TextureAtlases;

namespace MonoDreams.System.Draw;

public sealed class DynamicTextSystem(SpriteBatch batch, World world, RenderTarget2D renderTarget)
    : AEntitySetSystem<GameState>(world.GetEntities().With((in DynamicText t) => t.RenderTarget == renderTarget).With<Position>().AsSet(), true)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var text = ref entity.Get<DynamicText>();
        var layerDepth = DrawLayerDepth.GetLayerDepth(text.DrawLayer);
        ref var position = ref entity.Get<Position>();
        if (float.IsNaN(text.RevealStartTime)) text.RevealStartTime = state.TotalTime;
        var elapsedTime = state.TotalTime - text.RevealStartTime;
        var glyphs = TextProcessor.GetGlyphs(text, position.Current, 600);
        var revealedChars = text.IsRevealed ? glyphs.Count : 0;
        foreach (var (glyph, color) in glyphs)
        {
            if (!text.IsRevealed)
            {
                if (revealedChars / elapsedTime > text.RevealingSpeed) break;
                revealedChars++;
            }

            if (revealedChars == glyphs.Count)
            {
                text.IsRevealed = true;
            }
            
            if (glyph.FontRegion == null) continue;
            var origin1 = position.Current - glyph.Position;
            batch.Draw(glyph.FontRegion.TextureRegion, position.Current, color, 0.0f, origin1, Vector2.One * 0.75f, SpriteEffects.None, layerDepth);
        }
    }
}