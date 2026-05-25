using Microsoft.Xna.Framework;

namespace MonoDreams.Extensions.Monogame;

/// <summary>
/// Float-precision AABB for collision math. Eliminates integer truncation
/// that occurs when using <see cref="Rectangle"/> with float entity positions.
/// </summary>
public readonly struct CollisionRect(Vector2 position, Vector2 size)
{
    public readonly Vector2 Position = position;
    public readonly Vector2 Size = size;

    public float Left => Position.X;
    public float Top => Position.Y;
    public float Right => Position.X + Size.X;
    public float Bottom => Position.Y + Size.Y;
    public float Width => Size.X;
    public float Height => Size.Y;
    public Vector2 Center => Position + Size / 2f;

    public bool Intersects(CollisionRect other)
    {
        return Left < other.Right && Right > other.Left &&
               Top < other.Bottom && Bottom > other.Top;
    }

    /// <summary>
    /// Creates a CollisionRect from integer bounds offset by a float entity position,
    /// preserving sub-pixel precision that <see cref="RectangleExtensions.AtPosition"/> loses.
    /// </summary>
    public static CollisionRect FromBounds(Rectangle bounds, Vector2 entityPosition)
    {
        return new CollisionRect(
            bounds.Location.ToVector2() + entityPosition,
            bounds.Size.ToVector2());
    }
}
