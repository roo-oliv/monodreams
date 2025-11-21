using MonoDreams.Examples.Message;

namespace MonoDreams.Examples.Component;

/// <summary>
/// Singleton component that acts as a buffer for collision data between detection and resolution systems.
/// CollisionDetectionSystem writes collision data here, PhysicalCollisionResolutionSystem reads and processes it.
/// </summary>
public class CollisionBuffer
{
    public List<CollisionMessage> Collisions { get; set; } = new();
}
