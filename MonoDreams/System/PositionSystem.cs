using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.System;

public class PositionSystem<TPositionComponent>(World world, IParallelRunner runner)
    : AComponentSystem<GameState, TPositionComponent>(world, runner)
    where TPositionComponent : Position
{
    protected override void Update(GameState state, ref TPositionComponent position)
    {
        position.LastDelta = position.Delta;
        position.Last = position.Current;
    }
}

public class PositionSystem(World world, IParallelRunner runner) : PositionSystem<Position>(world, runner);
