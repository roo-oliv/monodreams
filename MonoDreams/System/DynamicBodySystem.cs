using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class DynamicBodySystem : AEntitySetSystem<GameState>
    {
        public DynamicBodySystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            ref var position = ref entity.Get<Position>();
            var currentPosition = position.TrueValue;
            var newPosition = 2 * currentPosition - position.LastValue + dynamicBody.Acceleration * state.Time * state.Time;
            position.DiscreteValue = newPosition.ToPoint();
            position.TrueValue = newPosition;
            position.LastValue = currentPosition;
        }
    }
}