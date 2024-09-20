using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System.Physics;

public class GravitySystem<TRigidBodyComponent, TPositionComponent>(World world, IParallelRunner parallelRunner, float worldGravity, float maxFallVelocity = 0)
    : AEntitySetSystem<GameState>(world.GetEntities().With<TPositionComponent>().With((in TRigidBodyComponent b) => b.Gravity.active).AsSet(), parallelRunner)
    where TRigidBodyComponent : RigidBody
    where TPositionComponent : Position
{
    protected override void Update(GameState state, in Entity entity)
    {
        var gravity = worldGravity * entity.Get<TRigidBodyComponent>().Gravity.factor;
        var position = entity.Get<TPositionComponent>();
        var lastVerticalVelocity = state.LastTime != 0 ? position.LastDelta.Y / state.LastTime : 0;
        position.Current.Y += (lastVerticalVelocity + gravity * state.Time) * state.Time;  // S_1N += (V_0 + a * dT) * dT
        if (maxFallVelocity > 0 && position.Delta.Y / state.Time > maxFallVelocity) {
            position.Current.Y = position.Last.Y + maxFallVelocity * state.Time;  // S_1N = S_0 + maxFallVelocity * dT 
        }
    }
}

public class GravitySystem<TRigidBodyComponent>(World world, IParallelRunner parallelRunner, float worldGravity, float maxFallVelocity = 0)
    : GravitySystem<TRigidBodyComponent, Position>(world, parallelRunner, worldGravity, maxFallVelocity)
    where TRigidBodyComponent : RigidBody;

public class GravitySystem(World world, IParallelRunner parallelRunner, float worldGravity, float maxFallVelocity = 0)
    : GravitySystem<RigidBody, Position>(world, parallelRunner, worldGravity, maxFallVelocity);
