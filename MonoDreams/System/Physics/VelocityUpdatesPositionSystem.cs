using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System.Physics;

public class VelocityUpdatesPositionSystem<TPositionComponent, TVelocityComponent>(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<TPositionComponent>().With<TVelocityComponent>().AsSet(), parallelRunner)
    where TPositionComponent : Position
    where TVelocityComponent : Velocity
{
    protected override void Update(GameState state, in Entity entity)
    {
        var position = entity.Get<TPositionComponent>();
        var velocity = entity.Get<TVelocityComponent>().Current;
        position.Current += velocity * state.Time;  // S_1 = S_0 + V * dT
    }
}

public class VelocityUpdatesPositionSystem<TPositionComponent>(World world, IParallelRunner parallelRunner)
    : VelocityUpdatesPositionSystem<TPositionComponent, Velocity>(world, parallelRunner)
    where TPositionComponent : Position;

public class VelocityUpdatesPositionSystem(World world, IParallelRunner parallelRunner)
    : VelocityUpdatesPositionSystem<Position, Velocity>(world, parallelRunner);
