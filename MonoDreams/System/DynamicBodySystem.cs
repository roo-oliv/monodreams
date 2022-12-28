using System;
using System.ComponentModel.DataAnnotations;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;
using NotImplementedException = System.NotImplementedException;

namespace MonoDreams.System
{
    public class DynamicBodySystem : AEntitySetSystem<GameState>
    {
        private const int MaxFallVelocity = 2900;

        public DynamicBodySystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<DynamicBody>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref var dynamicBody = ref entity.Get<DynamicBody>();
            ref var movementController = ref entity.Get<MovementController>();
            if (movementController is not null) ResolveMovement(dynamicBody, movementController, state);
            ResolveGravity(dynamicBody, state);
        }

        private static void ResolveMovement(DynamicBody body, MovementController movement, GameState state)
        {
            body.NextPosition = body.CurrentPosition + movement.Velocity * state.Time;  // S_1 = S_0 + V * t
            movement.Clear();
        }

        private static void ResolveGravity(DynamicBody body, GameState state)
        {
            var lastYVelocity = 0f;
            if (state.LastTime != 0)
            {
                lastYVelocity = (body.CurrentPosition.Y - body.LastPosition.Y) / state.LastTime;   // V = (S_1 - S_0) / t
            }

            var yVelocity = 0f;
            if (state.Time != 0)
            {
                yVelocity = (body.NextPosition.Y - body.CurrentPosition.Y) / state.Time; // V = (S_1 - S_0) / t
            }

            var gravityVelocity = lastYVelocity + body.Gravity * state.Time;  // V_1 = V_0 + a * t
            yVelocity = Math.Min(yVelocity + gravityVelocity, MaxFallVelocity);
            body.NextPosition.Y = body.CurrentPosition.Y + yVelocity * state.Time;  // S_1 = S_0 + V * t
            
            if (!body.IsJumping || (body.IsJumping && yVelocity > 0))
            {
                body.IsJumping = false;
                body.Gravity = DynamicBody.WorldGravity;
            }
        }
    }
}