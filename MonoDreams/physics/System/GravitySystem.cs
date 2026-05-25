using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System.Physics;

public class GravitySystem<TRigidBodyComponent, TVelocityComponent>(World world, IParallelRunner parallelRunner, float worldGravity, float maxFallVelocity = 0)
    : AEntitySetSystem<GameState>(world.GetEntities().With<TVelocityComponent>().With((in TRigidBodyComponent b) => b.Gravity.active).AsSet(), parallelRunner)
    where TRigidBodyComponent : RigidBodyComponent
    where TVelocityComponent : VelocityComponent
{
    protected override void Update(GameState state, in Entity entity)
    {
        var gravity = worldGravity * entity.Get<TRigidBodyComponent>().Gravity.factor;
        var velocity = entity.Get<TVelocityComponent>();
        velocity.Current.Y += gravity * state.Time;  //  V_1 = V_0 + a * dT
        if (maxFallVelocity > 0 && velocity.Current.Y > maxFallVelocity) {
            velocity.Current.Y = maxFallVelocity;
        }
    }
}

public class GravitySystem<TRigidBodyComponent>(World world, IParallelRunner parallelRunner, float worldGravity, float maxFallVelocity = 0)
    : GravitySystem<TRigidBodyComponent, VelocityComponent>(world, parallelRunner, worldGravity, maxFallVelocity)
    where TRigidBodyComponent : RigidBodyComponent;

public class GravitySystem(World world, IParallelRunner parallelRunner, float worldGravity, float maxFallVelocity = 0)
    : GravitySystem<RigidBodyComponent, VelocityComponent>(world, parallelRunner, worldGravity, maxFallVelocity);
