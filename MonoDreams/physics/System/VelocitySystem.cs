using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System.Physics;

public class TransformVelocitySystem<TVelocityComponent>(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<TransformComponent>().With<TVelocityComponent>().AsSet(), parallelRunner)
    where TVelocityComponent : VelocityComponent
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<TransformComponent>();
        var velocity = entity.Get<TVelocityComponent>();
        transform.Position += velocity.Current * state.Time;  // S_1 = S_0 + V * dT
        velocity.Last = velocity.Current;
    }
}

public class TransformVelocitySystem(World world, IParallelRunner parallelRunner)
    : TransformVelocitySystem<VelocityComponent>(world, parallelRunner);
