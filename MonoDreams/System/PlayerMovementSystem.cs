using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class PlayerMovementSystem : AEntitySetSystem<GameState>
    {
        private const int WalkAcceleration = 300;
        private const int JumpAcceleration = 70;

        public PlayerMovementSystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().With<PlayerInput>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            // ref var velocity = ref entity.Get<Velocity>();
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            ref var playerInput = ref entity.Get<PlayerInput>();
            if (playerInput.Left.Active)
            {
                dynamicBody.Acceleration.X = -WalkAcceleration;
            }
            else if (playerInput.Right.Active)
            {
                dynamicBody.Acceleration.X = WalkAcceleration;
            }
            else
            {
                dynamicBody.Acceleration.X = 0;
            }
            if (dynamicBody.IsRiding && playerInput.Jump.Active)
            {
                dynamicBody.Acceleration.Y = -JumpAcceleration;
                dynamicBody.IsRiding = false;
                dynamicBody.IsJumping = true;
            }
        }
    }
}