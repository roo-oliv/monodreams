using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.Text;
using MonoGame.Extended.TextureAtlases;

namespace MonoDreams.System;

public sealed class DynamicTextSystem : AEntitySetSystem<GameState>
{
    private readonly Camera _camera;
    private readonly SpriteBatch _batch;

    public DynamicTextSystem(Camera camera, SpriteBatch batch, World world)
        : base(world.GetEntities().With<DynamicText>().With<Position>().AsSet(), true)
    {
        _camera = camera;
        _batch = batch;
    }

    protected override void PreUpdate(GameState state)
    {
        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearWrap,
            DepthStencilState.None,
            RasterizerState.CullNone,
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var text = ref entity.Get<DynamicText>();
        ref var position = ref entity.Get<Position>();
        if (float.IsNaN(text.RevealStartTime)) text.RevealStartTime = state.TotalTime;
        float elapsedTime = state.TotalTime - text.RevealStartTime;
        var glyphs = TextProcessor.GetGlyphs(text, position.CurrentLocation, 600);
        int revealedChars = text.IsRevealed ? glyphs.Count : 0;
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
            Vector2 origin1 = position.CurrentLocation - glyph.Position;
            _batch.Draw(glyph.FontRegion.TextureRegion, position.CurrentLocation, color, 0.0f, origin1, Vector2.One, SpriteEffects.None, 0.0f);
        }
    }

    protected override void PostUpdate(GameState state) => _batch.End();

    
}