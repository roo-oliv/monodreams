using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System.Physics;

public class VelocitySystem<TPositionComponent, TVelocityComponent>(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<TPositionComponent>().With<TVelocityComponent>().AsSet(), parallelRunner)
    where TPositionComponent : Position
    where TVelocityComponent : Velocity
{
    protected override void Update(GameState state, in Entity entity)
    {
        var position = entity.Get<TPositionComponent>();
        var velocity = entity.Get<TVelocityComponent>();
        position.Current += velocity.Current * state.Time;  // S_1 = S_0 + V * dT
        velocity.Last = velocity.Current;
    }
}

public class VelocitySystem<TPositionComponent>(World world, IParallelRunner parallelRunner)
    : VelocitySystem<TPositionComponent, Velocity>(world, parallelRunner)
    where TPositionComponent : Position;

public class VelocitySystem(World world, IParallelRunner parallelRunner)
    : VelocitySystem<Position, Velocity>(world, parallelRunner);

// Transform-based velocity system
public class TransformVelocitySystem<TVelocityComponent>(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<Transform>().With<TVelocityComponent>().AsSet(), parallelRunner)
    where TVelocityComponent : Velocity
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<Transform>();
        var velocity = entity.Get<TVelocityComponent>();
        transform.Position += velocity.Current * state.Time;  // S_1 = S_0 + V * dT
        velocity.Last = velocity.Current;
    }
}

public class TransformVelocitySystem(World world, IParallelRunner parallelRunner)
    : TransformVelocitySystem<Velocity>(world, parallelRunner);
