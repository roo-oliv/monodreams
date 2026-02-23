using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

/// <summary>
/// System that snaps entities between base rotation and an offset state on a global timer.
/// All entities toggle on the same frame for a synchronized stop-motion effect.
/// </summary>
[With(typeof(StopMotionEffect), typeof(Transform))]
public class StopMotionEffectSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var effect = ref entity.Get<StopMotionEffect>();
        ref var transform = ref entity.Get<Transform>();

        var phase = state.TotalTime % (2 * effect.CycleDuration);
        var rotated = phase < effect.CycleDuration;

        var target = rotated ? effect.BaseRotation + effect.OffsetRadians : effect.BaseRotation;

        // Only write when value actually changes to avoid unnecessary dirty flags
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (transform.Rotation != target)
            transform.Rotation = target;
    }
}
