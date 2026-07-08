#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>
/// The pure, GraphicsDevice-free geometry behind the camera-rig glyph (UX2-E) — separated from
/// <c>EditorCameraRig</c> (which owns the entity + the world/GraphicsDevice emission) so the math is
/// directly unit-testable. Mirrors the <c>CameraNav</c> / <c>GizmoTransform</c> / <c>OverlayProjection</c>
/// split: exactly one source of truth for "where the authored camera's frustum sits in the world" and
/// "does the free VIEW currently match the rig".
///
/// <para>The frustum world-rect is the region the authored game camera would show: a
/// <c>virtualResolution / zoom</c> box CENTRED on the rig position — the same relation
/// <c>Camera.ViewSize</c> / <c>Camera.VirtualScreenBounds</c> use for the live camera. The overlay
/// draws it as bounds + the X of corner-connecting diagonals (Blender's off-camera glyph); the
/// selection folds the same rect's border into the pick.</para>
/// </summary>
public static class CameraRigGlyph
{
    /// <summary>How close (WORLD units) the free VIEW position must be to the rig position to count as
    /// "you ARE the camera" (glyph hides). Half a world unit — sub-pixel at zoom 1 — so a snap
    /// (<c>Camera := rig</c>) reads as matched while any real pan un-matches it. Documented, autonomous.</summary>
    public const float PositionEpsilon = 0.5f;

    /// <summary>How close the VIEW zoom must be to the rig zoom to count as matched. 1e-3 on a
    /// 0.25..4.0 zoom range — tight enough that one scroll notch un-matches, loose enough that a
    /// snap's exact copy always matches.</summary>
    public const float ZoomEpsilon = 1e-3f;

    /// <summary>
    /// The four world-space corners (TL, TR, BR, BL) of the authored camera's frustum: a
    /// <paramref name="virtualWidth"/> × <paramref name="virtualHeight"/> box scaled by
    /// <c>1 / <paramref name="zoom"/></c> and centred on <paramref name="center"/>. A non-positive zoom
    /// degrades to 1 (never divides by zero). The corner order matches
    /// <c>ProxyGeometry.BoxWorldCorners</c> so downstream stroke/pick code treats it identically.
    /// </summary>
    public static Vector2[] FrustumWorldCorners(Vector2 center, float zoom, int virtualWidth, int virtualHeight)
    {
        var z = zoom > 0f ? zoom : 1f;
        var halfW = virtualWidth / z * 0.5f;
        var halfH = virtualHeight / z * 0.5f;
        return new[]
        {
            new Vector2(center.X - halfW, center.Y - halfH), // TL
            new Vector2(center.X + halfW, center.Y - halfH), // TR
            new Vector2(center.X + halfW, center.Y + halfH), // BR
            new Vector2(center.X - halfW, center.Y + halfH), // BL
        };
    }

    /// <summary>
    /// Whether the free VIEW (<paramref name="viewPos"/>/<paramref name="viewZoom"/>) currently matches
    /// the rig (<paramref name="rigPos"/>/<paramref name="rigZoom"/>) within the epsilons — the "you ARE
    /// the camera" test that hides the glyph. Position compares by squared distance so it is isotropic.
    /// </summary>
    public static bool ViewMatchesRig(Vector2 viewPos, float viewZoom, Vector2 rigPos, float rigZoom,
        float posEpsilon = PositionEpsilon, float zoomEpsilon = ZoomEpsilon)
        => Vector2.DistanceSquared(viewPos, rigPos) <= posEpsilon * posEpsilon
           && MathF.Abs(viewZoom - rigZoom) <= zoomEpsilon;
}
