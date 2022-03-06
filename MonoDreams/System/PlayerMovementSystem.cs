using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class PlayerMovementSystem : AEntitySetSystem<GameState>
    {
        private const int JumpVelocity = 900;
        private const int MaxWalkVelocity = 70;
        private const int WalkAcceleration = 1;
        private const int JumpAcceleration = 15000;

        public PlayerMovementSystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().With<PlayerInput>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            // ref var velocity = ref entity.Get<Velocity>();
            ref var position = ref entity.Get<Position>();
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            ref var playerInput = ref entity.Get<PlayerInput>();

            // var horizontalAcceleration = 0;
            // if (playerInput.Left.Active) horizontalAcceleration = -WalkAcceleration;
            // else if (playerInput.Right.Active) horizontalAcceleration = WalkAcceleration;
            //
            // // var horizontalIncrement = horizontalVelocity * state.Time;
            // // position.ArtificialIncrement(horizontalIncrement, 0);
            // var newPosition = 2 * position.NextValue - position.CurrentValue + new Vector2(horizontalAcceleration, 0) * state.Time * state.Time;
            // position.NextValue = newPosition;
            // if (Math.Abs(position.NextValue.X - position.CurrentValue.X) / state.Time > MaxWalkVelocity)
            // {
            //     var sign = position.NextValue.X > position.CurrentValue.X ? 1 : -1;
            //     position.NextValue.X = sign * MaxWalkVelocity * state.Time + position.CurrentValue.X;
            // }
            var inputWalkVelocity = 0;
            if (playerInput.Left.Active) inputWalkVelocity -= MaxWalkVelocity;
            if (playerInput.Right.Active) inputWalkVelocity += MaxWalkVelocity;
            if (inputWalkVelocity != 0)
            {
                var preComputedHVelocity = (position.NextValue.X - position.CurrentValue.X) / state.Time;
                // if (preComputedHVelocity/Math.Abs(preComputedHVelocity) == inputWalkVelocity/Math.Abs(inputWalkVelocity))
                // {
                //     if (preComputedHVelocity > 1)
                //     {
                //         finalVelocity = Math.Max(preComputedHVelocity, inputWalkVelocity);
                //     }
                //     else
                //     {
                //         position.NextValue.X = Math.Min(preComputedHVelocity, inputWalkVelocity);
                //     }
                // }
                // else
                // {
                position.NextValue.X = position.CurrentValue.X + (preComputedHVelocity + inputWalkVelocity) * state.Time;
                // }
            }

            // if (dynamicBody.IsRiding && playerInput.Jump.Active)
            // {
            //     var newPosition2 = 2 * position.NextValue - position.CurrentValue + new Vector2(0, -JumpAcceleration) * state.Time * state.Time;
            //     position.NextValue = newPosition2;
            //     dynamicBody.IsRiding = false;
            //     dynamicBody.IsJumping = true;
            // }
            if (dynamicBody.IsRiding && playerInput.Jump.Active)
            {
                position.NextValue.Y = position.CurrentValue.Y + JumpVelocity * state.Time;
                dynamicBody.IsJumping = true;
                dynamicBody.IsRiding = false;
            }
            else if (dynamicBody.IsJumping && !playerInput.Jump.Active)
            {
                dynamicBody.IsJumping = false;
            }
        }
    }
}