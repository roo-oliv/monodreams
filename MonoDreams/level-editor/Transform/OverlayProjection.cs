#nullable enable
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>
/// The pure world/authoring → <b>screen</b> mapping the editor overlays (gizmo handles, selection
/// outline, collider-proxy outlines) are emitted through. The overlays render on the
/// native-resolution <c>RenderTargetID.Editor</c> layer (composited 1:1 with the window, in
/// device pixels), so their geometry must be baked in screen pixels: a world point goes through
/// the camera's view matrix into render coordinates, then through the same aspect-fit mapping
/// <c>FinalDrawSystem</c> composites the game targets with —
/// <c>screen = render × (Destination.Size / VirtualSize) + Destination.Location</c>. An
/// authoring-space point (a <c>UI</c>/<c>HUD</c>/<c>Scroll</c>-target entity's coordinates) skips
/// the camera's POSE (position, zoom, rotation) and takes its
/// <see cref="Camera.RenderScale"/> — the authoring → render factor
/// <c>ViewportManager.LayoutCamera</c> applies to those very passes, and exactly identity in a
/// single-space game — before the same aspect-fit step.
///
/// <para><b>Sizes stay in authoring pixels.</b> Handle radii / line thicknesses are authored in
/// layout pixels (the same constants as before this projection existed) and scaled by
/// <see cref="ToScreenSize"/> — the render scale (authoring px → render px) and the aspect-fit
/// factor (render px → screen px), <b>never</b> the camera zoom. In a single-space game the render
/// scale is 1, so that is the fit factor alone. That preserves the exact apparent size the old
/// world-space <c>1/Zoom</c>-compensated overlays had (world × zoom × renderScale × fit ==
/// authoring × renderScale × fit), while the geometry now rasterizes directly at device
/// resolution instead of being drawn at virtual resolution and upscaled — which is the whole
/// point: zooming the camera moves/points the geometry but never fattens or thins the lines, and
/// a HiDPI backbuffer (see <c>EditorHiDpi</c>) sharpens them for free because
/// <c>DestinationRectangle</c> is already in device pixels.</para>
///
/// <para><b>Hit-testing is out of scope.</b> Selection picking and the gizmo's handle hit-tests
/// stay in world/virtual space exactly as before — this seam changes only where the VISUALS are
/// emitted. World-free and window-free (plain matrices + rectangles), unit-testable like
/// <see cref="GizmoTransform"/>.</para>
/// </summary>
public readonly struct OverlayProjection
{
    private readonly Matrix _toVirtual;
    private readonly float _scale;
    private readonly Vector2 _offset;
    // Render pixels per authoring unit (rendering — "Authoring space and render space are
    // distinct"): 1 in a single-space game. Sizes are authored in LAYOUT units, so ToScreenSize
    // lifts them into render space before the aspect-fit factor; the world path needs no extra
    // factor because the camera's view matrix already carries it.
    private readonly float _renderScale;

    /// <summary>The screen-space rectangle of the game viewport (the aspect-fit destination) —
    /// the bounds overlay geometry is clipped to so it never draws over the chrome margins or the
    /// letterbox bars (see <see cref="OverlayMeshClip"/>).</summary>
    public Rectangle Viewport { get; }

    private OverlayProjection(Matrix toVirtual, float scale, Vector2 offset, Rectangle viewport,
        float renderScale)
    {
        _toVirtual = toVirtual;
        _scale = scale;
        _offset = offset;
        Viewport = viewport;
        _renderScale = renderScale;
    }

    /// <summary>
    /// The projection for the coordinate space of <paramref name="space"/>: <c>Main</c>-target
    /// entities are world-space (project through <paramref name="camera"/>'s view matrix, then
    /// aspect-fit); every other scene target is authoring-space (the camera's render scale — NOT
    /// its pose — then aspect-fit; in a single-space game that is the aspect fit alone). A null
    /// <paramref name="viewportManager"/> (world-free unit tests) degrades to the identity
    /// aspect-fit — screen == virtual, viewport = the camera's virtual bounds — so the code path
    /// is identical, only the mapping is trivial.
    /// </summary>
    public static OverlayProjection For(RenderTargetID space, Camera camera, ViewportManager? viewportManager)
    {
        // Main: world → render pixels through the view matrix (which already carries the render
        // scale). Screen-space targets: authoring → render pixels is the render scale alone — the
        // same mapping ViewportManager.LayoutCamera applies to those passes. Identity when the game
        // is single-space.
        var toVirtual = space == RenderTargetID.Main
            ? camera.GetViewTransformationMatrix()
            : Matrix.CreateScale(camera.RenderScale, camera.RenderScale, 1f);
        if (viewportManager == null)
            return new OverlayProjection(
                toVirtual, 1f, Vector2.Zero,
                new Rectangle(0, 0, camera.VirtualWidth, camera.VirtualHeight), camera.RenderScale);

        var destination = viewportManager.DestinationRectangle;
        // Aspect-fit preserves the ratio, so X and Y scale are equal (up to the destination
        // rectangle's integer rounding); a single uniform factor keeps strokes isotropic.
        var scale = destination.Width / (float)viewportManager.VirtualWidth;
        return new OverlayProjection(toVirtual, scale, new Vector2(destination.X, destination.Y),
            destination, viewportManager.RenderScale);
    }

    /// <summary>Maps a point of the source space (world or virtual — per the factory) to screen
    /// pixels on the Editor layer.</summary>
    public Vector2 ToScreen(Vector2 point)
        => Vector2.Transform(point, _toVirtual) * _scale + _offset;

    /// <summary>Maps a size authored in AUTHORING pixels (handle radius, stroke thickness) to
    /// screen pixels: the render scale and the aspect-fit factor only — camera zoom never changes
    /// emitted sizes. In a single-space game the render scale is 1, so this is the aspect-fit factor
    /// alone, exactly as before.</summary>
    public float ToScreenSize(float virtualPixels) => virtualPixels * _renderScale * _scale;
}
