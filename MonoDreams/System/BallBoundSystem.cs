using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class BallBoundSystem : AEntitySetSystem<GameState>
    {
        public BallBoundSystem(World world)
            : base(world.GetEntities().With<Ball>().With<Position>().With<Velocity>().AsSet(), true)
        { }

        protected override void Update(GameState state, in Entity entity)
        {
            ref Position position = ref entity.Get<Position>();
            ref Velocity velocity = ref entity.Get<Velocity>();

            if (position.Value.X < 0)
            {
                position.Value.X *= -1;
                velocity.Value.X *= -1;
            }
            else if (position.Value.X > 790)
            {
                position.Value.X = 1580 - position.Value.X;
                velocity.Value.X *= -1;
            }

            if (position.Value.Y < 0)
            {
                position.Value.Y *= -1;
                velocity.Value.Y *= -1;
            }
            else if (position.Value.Y > 600)
            {
                entity.Dispose();
            }
        }
    }
}