using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

/// <summary>
/// System that updates orbital motion for any entity with an OrbitalMotion component.
/// </summary>
[With(typeof(OrbitalMotion), typeof(Transform))]
public class OrbSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var motion = ref entity.Get<OrbitalMotion>();
        ref var transform = ref entity.Get<Transform>();

        // Update angle (negative speed = clockwise)
        motion.Angle -= motion.Speed * state.Time;

        // Calculate new local position using trigonometry
        transform.Position = new Vector2(
            MathF.Cos(motion.Angle) * motion.Radius + motion.CenterOffset.X,
            MathF.Sin(motion.Angle) * motion.Radius + motion.CenterOffset.Y);
    }
}
