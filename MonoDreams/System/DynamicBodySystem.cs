using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

// public class DynamicBodySystem : AEntitySetSystem<GameState>
// {
//     private const int MaxFallVelocity = 8000;
//
//     public DynamicBodySystem(World world, IParallelRunner runner)
//         : base(world.GetEntities().With<DynamicBody>().AsSet(), runner)
//     {
//             
//     }
//
//     protected override void Update(GameState state, in Entity entity)
//     {
//         ref var dynamicBody = ref entity.Get<DynamicBody>();
//         ref var position = ref entity.Get<Position>();
//         ref var movementController = ref entity.Get<MovementController>();
//         if (movementController is not null) ResolveMovement(ref position, ref movementController, state);
//         ResolveGravity(ref position, ref dynamicBody, state);
//     }
//
//     private static void ResolveMovement(ref Position position, ref MovementController movement, in GameState state)
//     {
//         position.NextLocation = position.Current + movement.Velocity * state.Time;  // S_1 = S_0 + V * t
//         movement.Clear();
//     }
//
//     private static void ResolveGravity(ref Position position, ref DynamicBody body, in GameState state)
//     {
//         var lastYVelocity = 0f;
//         if (state.LastTime != 0)
//         {
//             lastYVelocity = (position.Current.Y - position.LastLocation.Y) / state.LastTime;   // V = (S_1 - S_0) / t
//         }
//
//         var yVelocity = 0f;
//         if (state.Time != 0)
//         {
//             yVelocity = (position.NextLocation.Y - position.Current.Y) / state.Time; // V = (S_1 - S_0) / t
//         }
//
//         var gravityVelocity = lastYVelocity + body.Gravity * state.Time;  // V_1 = V_0 + a * t
//         yVelocity = Math.Min(yVelocity + gravityVelocity, MaxFallVelocity);
//         position.NextLocation.Y = position.Current.Y + yVelocity * state.Time;  // S_1 = S_0 + V * t
//         if (!body.IsJumping || (body.IsJumping && yVelocity > 0))
//         {
//             body.IsJumping = false;
//             body.Gravity = DynamicBody.WorldGravity;
//         }
//     }
// }