using DefaultEcs;
using MonoDreams.Examples.Message;

namespace MonoDreams.Examples.System.Collision;

/// <summary>
/// Physical collision resolution system that works with Transform component.
/// Filters collisions to only handle physics-type collisions.
/// </summary>
public class TransformPhysicalCollisionResolutionSystem(World world)
    : TransformCollisionResolutionSystem<CollisionMessage>(world)
{
    [Subscribe]
    protected override void On(in CollisionMessage message)
    {
        if (message.Type == CollisionType.Physics) Collisions.Add(message);
    }
}
