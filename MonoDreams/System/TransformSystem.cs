using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public class TransformSystem<TTransformComponent>(World world, IParallelRunner runner)
    : AComponentSystem<GameState, TTransformComponent>(world, runner)
    where TTransformComponent : Transform
{
    protected override void Update(GameState state, ref TTransformComponent transform)
    {
        transform.LastPositionDelta = transform.PositionDelta;
        transform.LastPosition = transform.CurrentPosition;
        transform.LastRotationDelta = transform.RotationDelta;
        transform.LastRotation = transform.CurrentRotation;
    }
}

public class TransformSystem(World world, IParallelRunner runner) : TransformSystem<Transform>(world, runner);
