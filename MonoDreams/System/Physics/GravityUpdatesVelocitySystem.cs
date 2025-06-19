namespace MonoDreams.System.Physics;

// public class GravityUpdatesVelocitySystem<TGravityComponent, TVelocityComponent>(World world, IParallelRunner parallelRunner, float maxFallVelocity = 0)
//     : AEntitySetSystem<GameState>(world.GetEntities().With<Velocity>().With<Gravity>().AsSet(), parallelRunner)
//     where TGravityComponent : Gravity
//     where TVelocityComponent : Velocity
// {
//     protected override void Update(GameState state, in Entity entity)
//     {
//         var gravity = entity.Get<TGravityComponent>().Acceleration;
//         var velocity = entity.Get<TVelocityComponent>();
//         velocity.Current.Y += gravity * state.Time;  // V_1 = V_0 + a * dT
//         if (maxFallVelocity > 0 && velocity.Current.Y > maxFallVelocity) velocity.Current.Y = maxFallVelocity;
//     }
// }
//
// public class GravityUpdatesVelocitySystem<TGravityComponent>(World world, IParallelRunner parallelRunner, float maxFallVelocity = 0)
//     : GravityUpdatesVelocitySystem<TGravityComponent, Velocity>(world, parallelRunner, maxFallVelocity)
//     where TGravityComponent : Gravity;
//
// public class GravityUpdatesVelocitySystem(World world, IParallelRunner parallelRunner, float maxFallVelocity = 0)
//     : GravityUpdatesVelocitySystem<Gravity, Velocity>(world, parallelRunner, maxFallVelocity);
