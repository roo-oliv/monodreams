using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System;

public class PositionUpdatesItselfSystem<TPositionComponent>(World world, IParallelRunner runner)
    : AComponentSystem<GameState, TPositionComponent>(world, runner)
    where TPositionComponent : Position
{
    protected override void Update(GameState state, ref TPositionComponent position)
    {
        if (!position.HasChanged) return; 
        position.Last = position.Current;
    }
}

public class PositionUpdatesItselfSystem(World world, IParallelRunner runner) : PositionUpdatesItselfSystem<Position>(world, runner);
