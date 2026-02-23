using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

/// <summary>
/// System that snaps entities between base rotation and a rotated state on a global timer.
/// All entities toggle on the same frame for synchronized wobble.
/// </summary>
[With(typeof(RotationWobble), typeof(Transform))]
public class RotationWobbleSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var wobble = ref entity.Get<RotationWobble>();
        ref var transform = ref entity.Get<Transform>();

        var phase = state.TotalTime % (2 * wobble.CycleDuration);
        var rotated = phase < wobble.CycleDuration;

        var target = rotated ? wobble.BaseRotation + wobble.AmplitudeRadians : wobble.BaseRotation;

        // Only write when value actually changes to avoid unnecessary dirty flags
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (transform.Rotation != target)
            transform.Rotation = target;
    }
}
