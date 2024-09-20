using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System;

public class VelocityUpdatesItselfSystem<TVelocityComponent>(World world, IParallelRunner runner)
    : AComponentSystem<GameState, TVelocityComponent>(world, runner)
    where TVelocityComponent : Velocity
{
    protected override void Update(GameState state, ref TVelocityComponent velocity)
    {
        if (!velocity.HasChanged) return; 
        velocity.Last = velocity.Current;
    }
}

public class VelocityUpdatesItselfSystem(World world, IParallelRunner runner) : VelocityUpdatesItselfSystem<Velocity>(world, runner);
