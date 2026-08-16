using System;
using System.Runtime.InteropServices.Marshalling;
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

    private (float zoom, Vector2 cached) _viewSize = (float.NaN, Vector2.Zero);
    private (Vector2 position, Vector2 viewSize, Rectangle cached) _virtualScreenBounds;

    // Camera operates in virtual resolution space
    private readonly int _virtualWidth;
    private readonly int _virtualHeight;
    private readonly float _renderScale;
    private readonly int _layoutWidth;
    private readonly int _layoutHeight;

    /// <summary>The pass destination's pixel size — RENDER space. Must equal the render target this
    /// camera's pass draws into (see the rendering premise "A render pass's camera virtual
    /// resolution matches its destination").</summary>
    public int VirtualWidth => _virtualWidth;

    /// <inheritdoc cref="VirtualWidth"/>
    public int VirtualHeight => _virtualHeight;

    /// <summary>
    /// Render pixels per authoring unit — the ONLY place the two-space scale lives (see the
    /// rendering premise "Authoring space and render space are distinct; the scale lives only in
    /// the cameras"). It multiplies <see cref="Zoom"/> inside the view transform, so world/UI
    /// numbers stay authored in layout units while the pass fills a
    /// <see cref="VirtualWidth"/>×<see cref="VirtualHeight"/> target. 1 (the default) is the
    /// single-space game: authoring space IS render space and every matrix is unchanged.
    /// </summary>
    public float RenderScale => _renderScale;

    /// <summary>The camera's view size in AUTHORING units at zoom 1 — <see cref="VirtualWidth"/>
    /// divided by <see cref="RenderScale"/>. This is the number game code reasons in (screen-space
    /// extents, fit-zoom, frustum outlines); it is unchanged by a render-resolution move.</summary>
    public int LayoutWidth => _layoutWidth;

    /// <inheritdoc cref="LayoutWidth"/>
    public int LayoutHeight => _layoutHeight;

    /// <param name="virtualWidth">Destination (render target) width in pixels.</param>
    /// <param name="virtualHeight">Destination (render target) height in pixels.</param>
    /// <param name="renderScale">Render pixels per authoring unit; see <see cref="RenderScale"/>.
    /// Prefer <c>ViewportManager.CreateCamera()</c> / <c>ViewportManager.LayoutCamera</c> so the
    /// scale comes from the one place that owns both resolutions.</param>
    public Camera(int virtualWidth = 800, int virtualHeight = 600, float renderScale = 1f)
    {
        if (renderScale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(renderScale),
                $"Render scale must be positive (got {renderScale}).");
        _virtualWidth = virtualWidth;
        _virtualHeight = virtualHeight;
        _renderScale = renderScale;
        _layoutWidth = (int)MathF.Round(virtualWidth / renderScale);
        _layoutHeight = (int)MathF.Round(virtualHeight / renderScale);
        _zoom = 1.0f;
        _rotation = 0.0f;
        _position = Vector2.Zero;
    }

    /// <summary>The visible world extent in AUTHORING units: the destination size divided by the
    /// effective scale (<see cref="Zoom"/> × <see cref="RenderScale"/>). Culling and every world
    /// consumer therefore see the same numbers at any render resolution.</summary>
    public Vector2 ViewSize
    {
        get {
            if (_viewSize.zoom != _zoom)
            {
                var effective = _zoom * _renderScale;
                _viewSize = (_zoom, new Vector2(_virtualWidth / effective, _virtualHeight / effective));
            }
            return _viewSize.cached;
        }
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

    public Rectangle VirtualScreenBounds
    {
        get
        {
            if (_virtualScreenBounds.position != Position || _virtualScreenBounds.viewSize != ViewSize)
            {
                var bounds = new Rectangle( new Point((int)(Position.X - ViewSize.X * 0.5f), (int)(Position.Y - ViewSize.Y * 0.5f)), ViewSize.ToPoint());
                _virtualScreenBounds = (Position, ViewSize, bounds);
            }
            return _virtualScreenBounds.cached;
        }
    }

    public Matrix GetViewTransformationMatrix()
    {
        if (!_isViewTransformationDirty) return _transform;
        
        _camTranslationVector.X = -_position.X;
        _camTranslationVector.Y = -_position.Y;

        Matrix.CreateTranslation(ref _camTranslationVector, out _camTranslationMatrix);
        Matrix.CreateRotationZ(_rotation, out _camRotationMatrix);

        // The two-space scale lives HERE and nowhere else: authoring units → render pixels is
        // zoom × renderScale. At renderScale 1 (single-space game) this is the historical matrix.
        var effectiveScale = _zoom * _renderScale;
        _camScaleVector.X = effectiveScale;
        _camScaleVector.Y = effectiveScale;
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
    /// Converts a point of the AUTHORING screen space — <c>(0,0)</c> to
    /// <see cref="LayoutWidth"/>×<see cref="LayoutHeight"/>, which is exactly what
    /// <c>ViewportManager.MapMouse</c> returns — to world coordinates. The layout point is lifted
    /// into render space (× <see cref="RenderScale"/>) before the view matrix is inverted, so a
    /// render-resolution move never moves a picked world point. In a single-space game
    /// (<see cref="RenderScale"/> = 1) authoring space IS the virtual screen space and this is the
    /// historical behaviour.
    /// </summary>
    /// <param name="virtualScreenPosition">Coordinates in the authoring screen space (0,0 to LayoutWidth, LayoutHeight).</param>
    /// <returns>World coordinates.</returns>
    public Vector2 VirtualScreenToWorld(Vector2 virtualScreenPosition)
    {
        Matrix invViewMatrix = Matrix.Invert(GetViewTransformationMatrix());
        return Vector2.Transform(virtualScreenPosition * _renderScale, invViewMatrix);
    }
}