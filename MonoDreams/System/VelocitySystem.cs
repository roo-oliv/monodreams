using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class VelocitySystem : AEntitySetSystem<GameState>
    {
        public VelocitySystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<Velocity>().With<Position>().AsSet(), runner)
        { }

        protected override void Update(GameState state, in Entity entity)
        {
            ref Velocity velocity = ref entity.Get<Velocity>();
            ref Position position = ref entity.Get<Position>();

            Vector2 offset = velocity.Value * state.Time;
            Console.WriteLine(offset);

            position.Value.X += (int)offset.X;
            position.Value.Y += (int)offset.Y;
        }
    }
}