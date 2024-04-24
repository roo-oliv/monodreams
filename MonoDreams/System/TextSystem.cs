using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.System;

public sealed class TextSystem : AEntitySetSystem<GameState>
{
    private readonly Camera _camera;
    private readonly SpriteBatch _batch;

    public TextSystem(Camera camera, SpriteBatch batch, World world)
        : base(world.GetEntities().With<SimpleText>().With<Position>().AsSet(), true)
    {
        _camera = camera;
        _batch = batch;
    }

    protected override void PreUpdate(GameState state)
    {
        // _renderer.BeginDraw();
        _batch.Begin(
            SpriteSortMode.BackToFront,
            BlendState.AlphaBlend,
            SamplerState.LinearWrap,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var text = ref entity.Get<SimpleText>();
        ref var position = ref entity.Get<Position>();
        float layerDepth = DrawLayerDepth.GetLayerDepth(text.DrawLayer);
        _batch.DrawString(
            text.Font,
            text.Value,
            position.CurrentLocation,
            text.Color,
            0.0f,
            text.Origin, 
            Vector2.One, 
            SpriteEffects.None,
            layerDepth);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}