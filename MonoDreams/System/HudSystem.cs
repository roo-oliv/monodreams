using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class HudSystem : AEntitySetSystem<GameState>
    {
        private readonly SpriteBatch _batch;
        private readonly SpriteFont _font;

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
            _batch.DrawString(_font, "body.CurrentPosition: " + body.CurrentPosition, new Vector2(2600, 100), Color.White);
            _batch.DrawString(_font, "body.LastPosition: " + body.LastPosition, new Vector2(2600, 200), Color.White);
            var velocity = (body.CurrentPosition - body.LastPosition) / state.Time;
            _batch.DrawString(_font, "Calculated Velocity: " + velocity, new Vector2(2600, 300), Color.White);
            _batch.DrawString(_font, "body.Gravity: " + body.Gravity, new Vector2(2600, 400), Color.White);
            _batch.DrawString(_font, "body.IsRiding: " + body.IsRiding, new Vector2(2600, 500), Color.White);
            _batch.DrawString(_font, "body.IsJumping: " + body.IsJumping, new Vector2(2600, 600), Color.White);
            _batch.DrawString(_font, "playerInput.Jump.Active: " + playerInput.Jump.Active, new Vector2(2600, 700), Color.White);
        }

        protected override void PostUpdate(GameState state) => _batch.End();
    }
}