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
    // Backing fields
    private Vector2 _position;
    private Vector2 _lastPosition;
    private float _rotation;
    private Vector2 _scale;
    private Vector2 _origin;
    private Transform? _parent;

    // Cache for world transform calculations
    private Matrix? _worldMatrix;
    private bool _isDirty = true;

    public Transform(Vector2 position = default, float rotation = 0f, Vector2? scale = null, Vector2? origin = null)
    {
        _position = position;
        _lastPosition = position;
        _rotation = rotation;
        _scale = scale ?? Vector2.One;
        _origin = origin ?? Vector2.Zero;
    }

    #region Properties with Auto-Dirty

    /// <summary>
    /// Local position (relative to parent if Parent is set, otherwise world position).
    /// Setting this property automatically marks the transform as dirty.
    /// </summary>
    public Vector2 Position
    {
        get => _position;
        set { _position = value; SetDirty(); }
    }

    /// <summary>
    /// Position from the previous frame. Used for delta calculations.
    /// Does NOT mark the transform as dirty when set.
    /// </summary>
    public Vector2 LastPosition
    {
        get => _lastPosition;
        set => _lastPosition = value;
    }

    /// <summary>
    /// Local rotation in radians.
    /// Setting this property automatically marks the transform as dirty.
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set { _rotation = value; SetDirty(); }
    }

    /// <summary>
    /// Local scale.
    /// Setting this property automatically marks the transform as dirty.
    /// </summary>
    public Vector2 Scale
    {
        get => _scale;
        set { _scale = value; SetDirty(); }
    }

    /// <summary>
    /// Origin/pivot point for rotation and scaling.
    /// Setting this property automatically marks the transform as dirty.
    /// </summary>
    public Vector2 Origin
    {
        get => _origin;
        set { _origin = value; SetDirty(); }
    }

    /// <summary>
    /// Parent transform for hierarchical transforms.
    /// Setting this property automatically marks the transform as dirty.
    /// </summary>
    public Transform? Parent
    {
        get => _parent;
        set { _parent = value; SetDirty(); }
    }

    #endregion

    public bool HasMoved => _position != _lastPosition;
    public Vector2 Delta => _position - _lastPosition;

    /// <summary>
    /// Gets whether this transform needs its world matrix recalculated.
    /// </summary>
    public bool IsDirty => _isDirty;

    #region World Transform (Computed)

    /// <summary>
    /// Gets the world transformation matrix, combining this transform with its parent chain.
    /// The matrix includes origin offset, scale, rotation, and translation.
    /// </summary>
    public Matrix WorldMatrix
    {
        get
        {
            // Check if this transform is dirty OR if parent is dirty
            var parentDirty = _parent != null && _parent._isDirty;

            if (_isDirty || parentDirty || !_worldMatrix.HasValue)
            {
                // Build local matrix: Origin offset -> Scale -> Rotation -> Position
                var localMatrix =
                    Matrix.CreateTranslation(-_origin.X, -_origin.Y, 0) *
                    Matrix.CreateScale(_scale.X, _scale.Y, 1) *
                    Matrix.CreateRotationZ(_rotation) *
                    Matrix.CreateTranslation(_position.X, _position.Y, 0);

                // Combine with parent's world matrix if parent exists
                _worldMatrix = _parent != null
                    ? localMatrix * _parent.WorldMatrix
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
            return _parent != null
                ? _rotation + _parent.WorldRotation
                : _rotation;
        }
    }

    /// <summary>
    /// Gets the world scale by multiplying scales up the parent chain.
    /// </summary>
    public Vector2 WorldScale
    {
        get
        {
            return _parent != null
                ? _scale * _parent.WorldScale
                : _scale;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Marks this transform as dirty, forcing recalculation of the world matrix on next access.
    /// This is called automatically when properties are set, but can be called manually if needed.
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
        _lastPosition = _position;
    }

    #endregion

    #region Fluent Position Methods

    /// <summary>Sets the X component of position.</summary>
    public Transform SetPositionX(float x)
    {
        _position.X = x;
        SetDirty();
        return this;
    }

    /// <summary>Sets the Y component of position.</summary>
    public Transform SetPositionY(float y)
    {
        _position.Y = y;
        SetDirty();
        return this;
    }

    /// <summary>Adds a delta to position.</summary>
    public Transform Translate(Vector2 delta)
    {
        _position += delta;
        SetDirty();
        return this;
    }

    /// <summary>Adds x and y deltas to position.</summary>
    public Transform Translate(float x, float y)
    {
        _position.X += x;
        _position.Y += y;
        SetDirty();
        return this;
    }

    /// <summary>Adds a delta to the X component of position.</summary>
    public Transform TranslateX(float delta)
    {
        _position.X += delta;
        SetDirty();
        return this;
    }

    /// <summary>Adds a delta to the Y component of position.</summary>
    public Transform TranslateY(float delta)
    {
        _position.Y += delta;
        SetDirty();
        return this;
    }

    #endregion

    #region Fluent Rotation Methods

    /// <summary>Adds radians to rotation.</summary>
    public Transform Rotate(float radians)
    {
        _rotation += radians;
        SetDirty();
        return this;
    }

    #endregion

    #region Fluent Scale Methods

    /// <summary>Sets the X component of scale.</summary>
    public Transform SetScaleX(float x)
    {
        _scale.X = x;
        SetDirty();
        return this;
    }

    /// <summary>Sets the Y component of scale.</summary>
    public Transform SetScaleY(float y)
    {
        _scale.Y = y;
        SetDirty();
        return this;
    }

    /// <summary>Sets uniform scale.</summary>
    public Transform SetScale(float uniform)
    {
        _scale = new Vector2(uniform, uniform);
        SetDirty();
        return this;
    }

    /// <summary>Multiplies scale by a factor.</summary>
    public Transform ScaleBy(Vector2 factor)
    {
        _scale *= factor;
        SetDirty();
        return this;
    }

    /// <summary>Multiplies scale uniformly by a factor.</summary>
    public Transform ScaleBy(float factor)
    {
        _scale *= factor;
        SetDirty();
        return this;
    }

    #endregion

    #region Fluent Origin Methods

    /// <summary>Sets the X component of origin.</summary>
    public Transform SetOriginX(float x)
    {
        _origin.X = x;
        SetDirty();
        return this;
    }

    /// <summary>Sets the Y component of origin.</summary>
    public Transform SetOriginY(float y)
    {
        _origin.Y = y;
        SetDirty();
        return this;
    }

    #endregion
}
