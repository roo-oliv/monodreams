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
            ref var velocity = ref entity.Get<Velocity>();
            ref var position = ref entity.Get<Position>();
        
            var (realXOffset, realYOffset) = velocity.Value * state.Time + position.Reminder;
            var (discreteXOffset, discreteYOffset) = ((int)realXOffset, (int)realYOffset);
        
            position.Value.X += discreteXOffset;
            position.Value.Y += discreteYOffset;
            position.Reminder.X = realXOffset - discreteXOffset;
            position.Reminder.Y = realYOffset - discreteYOffset;
        }
    }
}