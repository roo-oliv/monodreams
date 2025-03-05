using DefaultEcs;
using MonoDreams.Examples.Message;
using MonoDreams.System.Collision;

namespace MonoDreams.Examples.System;

public class PhysicalCollisionResolutionSystem(World world) : CollisionResolutionSystem<CollisionMessage>(world)
{
    [Subscribe]
    protected override void On(in CollisionMessage message)
    {
        if (message.Type == CollisionType.Physics) Collisions.Add(message);
    }
}