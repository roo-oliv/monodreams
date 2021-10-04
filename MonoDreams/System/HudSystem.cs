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
            ref var velocity = ref entity.Get<Velocity>();
            _batch.DrawString(_font, "velocity: " + velocity.Value, new Vector2(1500, 200), Color.White);
        }

        protected override void PostUpdate(GameState state) => _batch.End();
    }
}