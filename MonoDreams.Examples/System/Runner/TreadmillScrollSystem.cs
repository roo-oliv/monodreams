using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.Runner;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Runner;

[With(typeof(TreadmillSegment), typeof(Transform))]
public class TreadmillScrollSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<Transform>();
        var segment = entity.Get<TreadmillSegment>();
        var segmentStride = RunnerConstants.TreadmillSegmentWidth + RunnerConstants.TreadmillSegmentGap;
        var totalWidth = RunnerConstants.TreadmillSegmentCount * segmentStride;

        if (segment.IsTopRow)
        {
            // Top row scrolls left, wraps to right
            transform.TranslateX(-RunnerConstants.TreadmillScrollSpeed * state.Time);
            if (transform.Position.X < -segmentStride)
            {
                transform.SetPositionX(transform.Position.X + totalWidth);
            }
        }
        else
        {
            // Bottom row scrolls right, wraps to left
            transform.TranslateX(RunnerConstants.TreadmillScrollSpeed * state.Time);
            if (transform.Position.X > totalWidth)
            {
                transform.SetPositionX(transform.Position.X - totalWidth);
            }
        }
    }
}
