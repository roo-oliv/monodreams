using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class HudSystem : AEntitySetSystem<GameState>
{
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly Camera _camera;
    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;

    public HudSystem(ResolutionIndependentRenderer renderer, Camera camera, SpriteBatch batch, SpriteFont font, World world)
        : base(world.GetEntities().With<PlayerInput>().AsSet(), true)
    {
        _renderer = renderer;
        _camera = camera;
        _batch = batch;
        _font = font;
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
        ref var body = ref entity.Get<DynamicBody>();
        ref var position = ref entity.Get<Position>();
        ref var playerInput = ref entity.Get<PlayerInput>();
        var x = _camera.Position.X + 2400;
        var y = _camera.Position.Y - 2100;
        var spacing = 80;
        var scale = Vector2.One;
        // _batch.DrawString(_font, "FPS: " + 1 / state.Time, new Vector2(XTextPosition, 10), Color.White);
        // _batch.DrawString(_font, "Real FPS: " + 1 / (state.TotalTime - state.LastTotalTime), new Vector2(XTextPosition, 80), Color.White);
        // _batch.DrawString(_font, "Time: " + state.Time, new Vector2(XTextPosition, 150), Color.White);
        // _batch.DrawString(_font, "LastTime: " + state.LastTime, new Vector2(XTextPosition, 220), Color.White);
        _batch.DrawString(_font, "TotalTime: " + state.TotalTime, new Vector2(x, y + 0), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        // _batch.DrawString(_font, "LastTotalTime: " + state.LastTotalTime, new Vector2(XTextPosition, 360), Color.White);
        // _batch.DrawString(_font, "TotalTimeDelta: " + (state.TotalTime - state.LastTotalTime), new Vector2(XTextPosition, 430), Color.White);
        _batch.DrawString(_font, "CurrentLocation: " + position.CurrentLocation, new Vector2(x, y + spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _batch.DrawString(_font, "LastLocation: " + position.LastLocation, new Vector2(x, y + 2 * spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        var velocity = (position.CurrentLocation - position.LastLocation) / state.Time;
        _batch.DrawString(_font, "Calculated Velocity: " + velocity, new Vector2(x, y + 3 * spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _batch.DrawString(_font, "Player Gravity: " + body.Gravity, new Vector2(x, y + 4 * spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _batch.DrawString(_font, "IsRiding: " + body.IsRiding, new Vector2(x, y + 5 * spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _batch.DrawString(_font, "IsJumping: " + body.IsJumping, new Vector2(x, y + 6 * spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _batch.DrawString(_font, "playerInput.Jump.Active: " + playerInput.Jump.Active, new Vector2(x, y + 7 * spacing), Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}