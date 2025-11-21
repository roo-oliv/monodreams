namespace MonoDreams.Examples.Component.Collision;

/// <summary>
/// Tracks if an entity is currently involved in a collision.
/// Updated by collision resolution system, used by debug visualization.
/// </summary>
public class CollisionState
{
    public bool IsColliding { get; set; }
    public int CollisionCount { get; set; }
}
