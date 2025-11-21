using Flecs.NET.Core;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Physics;

/// <summary>
/// Applies velocity to position using basic physics integration (S_1 = S_0 + V * dT).
/// Runs in the Physics phase after input/movement but before collision detection.
/// </summary>
public static class VelocitySystem
{
    public static void Register(World world)
    {
        world.System<Position, Velocity>("VelocitySystem")
            .Kind(PhysicsPhase)
            .Each((Iter it, int index, ref Position position, ref Velocity velocity) =>
            {
                var deltaTime = it.DeltaTime();

                // Apply velocity to position: S_1 = S_0 + V * dT
                position.Current += velocity.Current * deltaTime;

                // Track previous velocity for change detection
                velocity.Last = velocity.Current;
            });
    }
}
