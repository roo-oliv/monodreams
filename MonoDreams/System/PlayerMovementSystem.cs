using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class PlayerMovementSystem : AEntitySetSystem<GameState>
    {
        private const int JumpGravity = 4400;
        private const int JumpVelocity = -2100;
        private const int MaxWalkVelocity = 900;

        public PlayerMovementSystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().With<PlayerInput>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            ref var playerInput = ref entity.Get<PlayerInput>();
            ref var movementController = ref entity.Get<MovementController>();

            if (playerInput.Left.Active) movementController.Velocity.X -= MaxWalkVelocity;
            if (playerInput.Right.Active) movementController.Velocity.X += MaxWalkVelocity;

            if (dynamicBody.IsRiding && playerInput.Jump.JustActivated)
            {
                movementController.Velocity.Y = JumpVelocity;
                dynamicBody.IsJumping = true;
                dynamicBody.IsRiding = false;
                dynamicBody.Gravity = JumpGravity;
            }
            else if (dynamicBody.IsJumping && !playerInput.Jump.Active)
            {
                dynamicBody.IsJumping = false;
                dynamicBody.Gravity = DynamicBody.WorldGravity;
            }
        }
    }
}