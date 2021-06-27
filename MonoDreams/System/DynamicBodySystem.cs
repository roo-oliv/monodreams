using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class DynamicBodySystem : AEntitySetSystem<GameState>
    {
        private const int Gravity = 400;

        public DynamicBodySystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            if (dynamicBody.IsRiding)
            {
                return;
            }
            
            ref var velocity = ref entity.Get<Velocity>();
            velocity.Value.Y += Gravity * state.Time;
        }
    }
}