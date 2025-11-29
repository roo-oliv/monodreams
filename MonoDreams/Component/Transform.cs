#nullable enable
using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

/// <summary>
/// Represents the spatial transformation of an entity including position, rotation, scale, and origin.
/// Supports parent-child hierarchies where transforms are relative to a parent transform.
/// Extends the concept of Position with full transform capabilities.
/// </summary>
public class Transform
{
    // Local transform (relative to parent if Parent is set, otherwise world transform)
    public Vector2 Position;
    public Vector2 LastPosition;
    public float Rotation;
    public Vector2 Scale;
    public Vector2 Origin;

    // Hierarchy
    public Transform? Parent;

    // Cache for world transform calculations
    private Matrix? _worldMatrix;
    private bool _isDirty = true;

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
    /// Gets whether this transform needs its world matrix recalculated.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets the world transformation matrix, combining this transform with its parent chain.
    /// The matrix includes origin offset, scale, rotation, and translation.
    /// </summary>
    public Matrix WorldMatrix
    {
        get
        {
            // Check if this transform is dirty OR if parent is dirty
            var parentDirty = Parent != null && Parent._isDirty;

            if (_isDirty || parentDirty || !_worldMatrix.HasValue)
            {
                // Build local matrix: Origin offset -> Scale -> Rotation -> Position
                var localMatrix =
                    Matrix.CreateTranslation(-Origin.X, -Origin.Y, 0) *
                    Matrix.CreateScale(Scale.X, Scale.Y, 1) *
                    Matrix.CreateRotationZ(Rotation) *
                    Matrix.CreateTranslation(Position.X, Position.Y, 0);

                // Combine with parent's world matrix if parent exists
                _worldMatrix = Parent != null
                    ? localMatrix * Parent.WorldMatrix
                    : localMatrix;

                _isDirty = false;
            }

            return _worldMatrix.Value;
        }
    }

    /// <summary>
    /// Gets the world position by extracting translation from the world matrix.
    /// </summary>
    public Vector2 WorldPosition
    {
        get
        {
            var matrix = WorldMatrix;
            return new Vector2(matrix.M41, matrix.M42);
        }
    }

    /// <summary>
    /// Gets the world rotation by accumulating rotations up the parent chain.
    /// </summary>
    public float WorldRotation
    {
        get
        {
            return Parent != null
                ? Rotation + Parent.WorldRotation
                : Rotation;
        }
    }

    /// <summary>
    /// Gets the world scale by multiplying scales up the parent chain.
    /// </summary>
    public Vector2 WorldScale
    {
        get
        {
            return Parent != null
                ? Scale * Parent.WorldScale
                : Scale;
        }
    }

    /// <summary>
    /// Marks this transform as dirty, forcing recalculation of the world matrix on next access.
    /// Call this whenever Position, Rotation, Scale, Origin, or Parent changes.
    /// </summary>
    public void SetDirty()
    {
        _isDirty = true;
    }

    /// <summary>
    /// Updates LastPosition to current. Call this at the end of each frame.
    /// </summary>
    public void CommitPosition()
    {
        LastPosition = Position;
    }
}
