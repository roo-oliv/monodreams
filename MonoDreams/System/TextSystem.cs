using System;
using System.Diagnostics;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class TextSystem : AEntitySetSystem<GameState>
{
    private readonly Camera _camera;
    private readonly SpriteBatch _batch;

    public TextSystem(Camera camera, SpriteBatch batch, World world)
        : base(world.GetEntities().With<Text>().With<Position>().AsSet(), true)
    {
        _camera = camera;
        _batch = batch;
    }

    protected override void PreUpdate(GameState state)
    {
        // _renderer.BeginDraw();
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
        ref var text = ref entity.Get<Text>();
        ref var position = ref entity.Get<Position>();
        var font = text.SpriteFont;
        Vector2 origin = text.TextAlign switch
        {
            TextAlign.Left => Vector2.Zero,
            TextAlign.Center => new Vector2(font.MeasureString(text.Value).X / 2, 0),
            TextAlign.Right => new Vector2(font.MeasureString(text.Value).X, 0),
            _ => throw new ArgumentOutOfRangeException(),
        };
        _batch.DrawString(
            font,
            text.Value,
            position.CurrentLocation,
            text.Color,
            0.0f,
            origin,
            1,
            SpriteEffects.None,
            0.0f);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}