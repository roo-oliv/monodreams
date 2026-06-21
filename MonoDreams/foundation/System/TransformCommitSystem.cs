using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

/// <summary>
/// Commits transform changes by updating LastPosition.
/// Should run at the end of the frame, after all position modifications.
/// This is the TransformComponent-equivalent of PositionSystem.
/// </summary>
public class TransformCommitSystem(World world, IParallelRunner runner)
    : AComponentSystem<GameState, TransformComponent>(world, runner)
{
    protected override void Update(GameState state, ref TransformComponent transform)
    {
        transform.CommitPosition();
    }
}
