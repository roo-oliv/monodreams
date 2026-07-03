#nullable enable
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>
/// The pure world/virtual → <b>screen</b> mapping the editor overlays (gizmo handles, selection
/// outline, collider-proxy outlines) are emitted through. The overlays render on the
/// native-resolution <c>RenderTargetID.Editor</c> layer (composited 1:1 with the window, in
/// device pixels), so their geometry must be baked in screen pixels: a world point goes through
/// the camera's view matrix into virtual coordinates, then through the same aspect-fit mapping
/// <c>FinalDrawSystem</c> composites the game targets with —
/// <c>screen = virtual × (Destination.Size / VirtualSize) + Destination.Location</c>. A
/// virtual-space point (a <c>UI</c>/<c>HUD</c>/<c>Scroll</c>-target entity's coordinates) skips
/// the camera and takes only the aspect-fit step.
///
/// <para><b>Sizes stay in virtual pixels.</b> Handle radii / line thicknesses are authored in
/// virtual pixels (the same constants as before this projection existed) and scaled by
/// <see cref="ToScreenSize"/> — the aspect-fit factor only, <b>never</b> the camera zoom. That
/// preserves the exact apparent size the old world-space <c>1/Zoom</c>-compensated overlays had
/// (world × zoom × fit == virtual × fit), while the geometry now rasterizes directly at device
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

    /// <summary>The screen-space rectangle of the game viewport (the aspect-fit destination) —
    /// the bounds overlay geometry is clipped to so it never draws over the chrome margins or the
    /// letterbox bars (see <see cref="OverlayMeshClip"/>).</summary>
    public Rectangle Viewport { get; }

    private OverlayProjection(Matrix toVirtual, float scale, Vector2 offset, Rectangle viewport)
    {
        _toVirtual = toVirtual;
        _scale = scale;
        _offset = offset;
        Viewport = viewport;
    }

    /// <summary>
    /// The projection for the coordinate space of <paramref name="space"/>: <c>Main</c>-target
    /// entities are world-space (project through <paramref name="camera"/>'s view matrix, then
    /// aspect-fit); every other scene target is virtual-space (aspect-fit only). A null
    /// <paramref name="viewportManager"/> (world-free unit tests) degrades to the identity
    /// aspect-fit — screen == virtual, viewport = the camera's virtual bounds — so the code path
    /// is identical, only the mapping is trivial.
    /// </summary>
    public static OverlayProjection For(RenderTargetID space, Camera camera, ViewportManager? viewportManager)
    {
        var toVirtual = space == RenderTargetID.Main
            ? camera.GetViewTransformationMatrix()
            : Matrix.Identity;
        if (viewportManager == null)
            return new OverlayProjection(
                toVirtual, 1f, Vector2.Zero,
                new Rectangle(0, 0, camera.VirtualWidth, camera.VirtualHeight));

        var destination = viewportManager.DestinationRectangle;
        // Aspect-fit preserves the ratio, so X and Y scale are equal (up to the destination
        // rectangle's integer rounding); a single uniform factor keeps strokes isotropic.
        var scale = destination.Width / (float)viewportManager.VirtualWidth;
        return new OverlayProjection(toVirtual, scale, new Vector2(destination.X, destination.Y), destination);
    }

    /// <summary>Maps a point of the source space (world or virtual — per the factory) to screen
    /// pixels on the Editor layer.</summary>
    public Vector2 ToScreen(Vector2 point)
        => Vector2.Transform(point, _toVirtual) * _scale + _offset;

    /// <summary>Maps a size authored in virtual pixels (handle radius, stroke thickness) to
    /// screen pixels: the aspect-fit factor only — camera zoom never changes emitted sizes.</summary>
    public float ToScreenSize(float virtualPixels) => virtualPixels * _scale;
}
