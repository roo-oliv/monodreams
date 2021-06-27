using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class PlayerMovementSystem : AEntitySetSystem<GameState>
    {
        private const int WalkSpeed = 900;
        private const int JumpSpeed = 900;

        public PlayerMovementSystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<Velocity>().With<PlayerInput>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref var velocity = ref entity.Get<Velocity>();
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            ref var playerInput = ref entity.Get<PlayerInput>();
            if (playerInput.Left.JustActivated)
            {
                velocity.Value.X -= WalkSpeed;
            }
            if (playerInput.Left.JustReleased)
            {
                velocity.Value.X += WalkSpeed;
            }
            if (playerInput.Right.JustActivated)
            {
                velocity.Value.X += WalkSpeed;
            }
            if (playerInput.Right.JustReleased)
            {
                velocity.Value.X -= WalkSpeed;
            }
            if (dynamicBody.IsRiding && playerInput.Jump.JustReleased)
            {
                velocity.Value.Y -= JumpSpeed;
                dynamicBody.IsRiding = false;
            }
        }
    }
}