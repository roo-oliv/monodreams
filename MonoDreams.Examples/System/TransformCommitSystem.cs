using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

/// <summary>
/// Commits transform changes by updating LastPosition.
/// Should run at the end of the frame, after all position modifications.
/// This is the Transform-equivalent of PositionSystem.
/// </summary>
public class TransformCommitSystem(World world, IParallelRunner runner)
    : AComponentSystem<GameState, Transform>(world, runner)
{
    protected override void Update(GameState state, ref Transform transform)
    {
        transform.CommitPosition();
    }
}
