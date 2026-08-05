using DefaultEcs;
using MonoDreams.Message;

namespace MonoDreams.System.Collision;

/// <summary>
/// Physical collision resolution system that works with TransformComponent component.
/// Filters collisions to only handle physics-type collisions.
/// </summary>
public class TransformPhysicalCollisionResolutionSystem(World world)
    : TransformCollisionResolutionSystem<CollisionMessage>(world)
{
    // NO [Subscribe] here: the base class already annotates the virtual On, and DefaultEcs registers
    // every [Subscribe]-annotated method it finds walking the type hierarchy. Annotating the override
    // too registers the SAME (virtually dispatched) handler twice, so every CollisionMessage was
    // resolved twice per frame. The second pass re-runs the resolver on a body whose Delta already
    // contains the first pass' correction — harmless for a near-face swept block (the re-solve is a
    // zero-length correction), but destructive after a depenetration that exits ALONG the motion: the
    // swept re-solve then back-projects the body clean across the target. Overriding without the
    // attribute keeps the Physics-type filter (virtual dispatch) and handles each message once.
    protected override void On(in CollisionMessage message)
    {
        if (message.Type == CollisionType.Physics) Collisions.Add(message);
    }
}
