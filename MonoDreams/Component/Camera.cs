using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public class Camera
{
    private float _zoom;
    private float _rotation;
    private Vector2 _position;
    private Matrix _transform = Matrix.Identity;
    private bool _isViewTransformationDirty = true;
    private Matrix _camTranslationMatrix = Matrix.Identity;
    private Matrix _camRotationMatrix = Matrix.Identity;
    private Matrix _camScaleMatrix = Matrix.Identity;
    private Matrix _resTranslationMatrix = Matrix.Identity;

    private Vector3 _camTranslationVector = Vector3.Zero;
    private Vector3 _camScaleVector = Vector3.Zero;
    private Vector3 _resTranslationVector = Vector3.Zero;

    // Camera operates in virtual resolution space
    private readonly int _virtualWidth;
    private readonly int _virtualHeight;
    
    public Camera(int virtualWidth = 800, int virtualHeight = 600) // Use configured virtual size
    {
        _virtualWidth = virtualWidth;
        _virtualHeight = virtualHeight;
        _zoom = 1.0f;
        _rotation = 0.0f;
        _position = Vector2.Zero;
    }

    public Vector2 Position
    {
        get => _position;
        set
        {
            _position = value;
            _isViewTransformationDirty = true;
        }
    }

    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = value;
            if (_zoom < 0.1f)
            {
                _zoom = 0.1f;
            }
            _isViewTransformationDirty = true;
        }
    }

    public float Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value;
            _isViewTransformationDirty = true;
        }
    }

    public Matrix GetViewTransformationMatrix()
    {
        if (!_isViewTransformationDirty) return _transform;
        
        _camTranslationVector.X = -_position.X;
        _camTranslationVector.Y = -_position.Y;

        Matrix.CreateTranslation(ref _camTranslationVector, out _camTranslationMatrix);
        Matrix.CreateRotationZ(_rotation, out _camRotationMatrix);

        _camScaleVector.X = _zoom;
        _camScaleVector.Y = _zoom;
        _camScaleVector.Z = 1;

        Matrix.CreateScale(ref _camScaleVector, out _camScaleMatrix);

        // Translate origin to center of the virtual screen for rotation/zoom
        _resTranslationVector.X = _virtualWidth * 0.5f;
        _resTranslationVector.Y = _virtualHeight * 0.5f;
        _resTranslationVector.Z = 0;

        Matrix.CreateTranslation(ref _resTranslationVector, out _resTranslationMatrix);

        _transform = _camTranslationMatrix 
                     * _camRotationMatrix
                     * _camScaleMatrix
                     * _resTranslationMatrix;

        _isViewTransformationDirty = false;

        return _transform;
    }

    public void RecalculateTransformationMatrices()
    {
        _isViewTransformationDirty = true;
    }

    /// <summary>
    /// Converts virtual screen coordinates (e.g., mouse position scaled to virtual resolution)
    /// to world coordinates.
    /// </summary>
    /// <param name="virtualScreenPosition">Coordinates in the virtual screen space (0,0 to VirtualWidth, VirtualHeight).</param>
    /// <returns>World coordinates.</returns>
    public Vector2 VirtualScreenToWorld(Vector2 virtualScreenPosition)
    {
        Matrix invViewMatrix = Matrix.Invert(GetViewTransformationMatrix());
        return Vector2.Transform(virtualScreenPosition, invViewMatrix);
    }
}