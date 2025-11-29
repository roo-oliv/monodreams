using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

/// <summary>
/// Applies velocity to Transform.Position. Transform-specific version of VelocitySystem.
/// </summary>
public class TransformVelocitySystem(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<Transform>().With<Velocity>().AsSet(), parallelRunner)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<Transform>();
        ref var velocity = ref entity.Get<Velocity>();
        transform.Position += velocity.Current * state.Time; // S_1 = S_0 + V * dT
        transform.SetDirty();
        velocity.Last = velocity.Current;
    }
}
