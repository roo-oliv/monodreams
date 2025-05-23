namespace MonoDreams.Examples.Message.Level;

/// <summary>
/// A message published when the LevelCollisionGrid has been processed and
/// is available as a world component.
/// </summary>
public readonly struct CollisionGridReadyMessage
{
    // This message currently carries no data. Its publication is the notification.
    // It could be extended with a reference to the grid or level ID if needed.
}
