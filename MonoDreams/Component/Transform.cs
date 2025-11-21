using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

/// <summary>
/// Represents the spatial transformation of an entity including position, rotation, scale, and origin.
/// Extends the concept of Position with full transform capabilities.
/// </summary>
public class Transform
{
    public Vector2 Position;
    public Vector2 LastPosition;
    public float Rotation;
    public Vector2 Scale;
    public Vector2 Origin;

    public Transform(Vector2 position = default, float rotation = 0f, Vector2? scale = null, Vector2? origin = null)
    {
        Position = position;
        LastPosition = position;
        Rotation = rotation;
        Scale = scale ?? Vector2.One;
        Origin = origin ?? Vector2.Zero;
    }

    public bool HasMoved => Position != LastPosition;
    public Vector2 Delta => Position - LastPosition;

    /// <summary>
    /// Updates LastPosition to current. Call this at the end of each frame.
    /// </summary>
    public void CommitPosition()
    {
        LastPosition = Position;
    }
}
