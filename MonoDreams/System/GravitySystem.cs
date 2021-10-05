using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class GravitySystem : AEntitySetSystem<GameState>
    {
        private const int GravityAcceleration = 300;

        public GravitySystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            entity.Get<DynamicBody>().Acceleration.Y = GravityAcceleration;
        }
    }
}