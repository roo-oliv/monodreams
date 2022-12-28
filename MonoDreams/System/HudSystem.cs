using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class HudSystem : AEntitySetSystem<GameState>
{
    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;
    private const int XTextPosition = 200;

    public HudSystem(SpriteBatch batch, SpriteFont font, World world)
        : base(world.GetEntities().With<PlayerInput>().AsSet(), true)
    {
        _batch = batch;
        _font = font;
    }

    protected override void PreUpdate(GameState state) => _batch.Begin();

    protected override void Update(GameState state, in Entity entity)
    {
        ref var body = ref entity.Get<DynamicBody>();
        ref var playerInput = ref entity.Get<PlayerInput>();
        _batch.DrawString(_font, "FPS: " + 1 / state.Time, new Vector2(XTextPosition, 10), Color.White);
        _batch.DrawString(_font, "Real FPS: " + 1 / (state.TotalTime - state.LastTotalTime), new Vector2(XTextPosition, 80), Color.White);
        _batch.DrawString(_font, "Time: " + state.Time, new Vector2(XTextPosition, 150), Color.White);
        _batch.DrawString(_font, "LastTime: " + state.LastTime, new Vector2(XTextPosition, 220), Color.White);
        _batch.DrawString(_font, "TotalTime: " + state.TotalTime, new Vector2(XTextPosition, 290), Color.White);
        _batch.DrawString(_font, "LastTotalTime: " + state.LastTotalTime, new Vector2(XTextPosition, 360), Color.White);
        _batch.DrawString(_font, "TotalTimeDelta: " + (state.TotalTime - state.LastTotalTime), new Vector2(XTextPosition, 430), Color.White);
        // _batch.DrawString(_font, "body.CurrentPosition: " + body.CurrentPosition, new Vector2(XTextPosition, 150), Color.White);
        // _batch.DrawString(_font, "body.LastPosition: " + body.LastPosition, new Vector2(XTextPosition, 250), Color.White);
        // var velocity = (body.CurrentPosition - body.LastPosition) / state.Time;
        // _batch.DrawString(_font, "Calculated Velocity: " + velocity, new Vector2(XTextPosition, 350), Color.White);
        // _batch.DrawString(_font, "body.Gravity: " + body.Gravity, new Vector2(XTextPosition, 450), Color.White);
        // _batch.DrawString(_font, "body.IsRiding: " + body.IsRiding, new Vector2(XTextPosition, 550), Color.White);
        // _batch.DrawString(_font, "body.IsJumping: " + body.IsJumping, new Vector2(XTextPosition, 650), Color.White);
        // _batch.DrawString(_font, "playerInput.Jump.Active: " + playerInput.Jump.Active, new Vector2(XTextPosition, 750), Color.White);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}