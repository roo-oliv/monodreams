#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;

namespace MonoDreams.LevelEditor.Navigation;

/// <summary>
/// The pure, GraphicsDevice-free math behind the editor's camera navigation (pan / zoom / frame-scene),
/// separated from <c>CameraNavSystem</c> (which owns only the input read + the camera writes) so the
/// math is directly unit-testable without a world, a cursor, or a real <c>Camera</c>. Mirrors the
/// <c>GizmoTransform</c> split.
///
/// <para>Coordinate note: pan is computed in <b>world units</b>. A drag is a cursor displacement in
/// virtual-screen pixels; one world unit is <c>zoom</c> virtual pixels, so the world displacement is
/// <c>virtualDelta / zoom</c>. To keep the world point under the cursor pinned, the camera moves the
/// <b>opposite</b> way: <c>Position -= worldDelta</c> (drag right → camera moves left → content follows
/// the cursor right). This is the sign the test fixes.</para>
/// </summary>
public static class CameraNav
{
    /// <summary>The new camera position after a pan, given the cursor's <b>virtual-screen</b> delta this
    /// frame and the current zoom. The world point under the cursor stays under the cursor.</summary>
    public static Vector2 Pan(Vector2 position, Vector2 virtualDelta, float zoom)
    {
        var z = zoom > 0f ? zoom : 1f;
        // virtualDelta / zoom is the world distance the cursor swept; move the camera the opposite way
        // so the grabbed world point tracks the cursor.
        return position - virtualDelta / z;
    }

    /// <summary>
    /// The new zoom after a scroll step, clamped to <paramref name="min"/>..<paramref name="max"/>.
    /// Each notch (<paramref name="scrollNotches"/>, sign = direction) multiplies the zoom by
    /// <paramref name="stepFactor"/> (scroll in → ×factor, larger; scroll out → ÷factor, smaller), so
    /// zooming is geometric and symmetric (one in + one out returns to the start before clamping).
    /// </summary>
    public static float Zoom(float zoom, int scrollNotches, float stepFactor, float min, float max)
    {
        if (scrollNotches != 0 && stepFactor > 0f)
            zoom *= MathF.Pow(stepFactor, scrollNotches);
        return Math.Clamp(zoom, min, max);
    }

    /// <summary>
    /// The axis-aligned world-space bounding box of a set of sprite-quad corner arrays (each from
    /// <c>GizmoTransform.SpriteWorldQuad</c>), or <c>null</c> when there is no content. Used by
    /// frame-scene to find the region to centre on.
    /// </summary>
    public static Rectangle? ContentBounds(IEnumerable<Vector2[]> spriteQuads)
    {
        var has = false;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var quad in spriteQuads)
        {
            foreach (var c in quad)
            {
                has = true;
                if (c.X < minX) minX = c.X;
                if (c.Y < minY) minY = c.Y;
                if (c.X > maxX) maxX = c.X;
                if (c.Y > maxY) maxY = c.Y;
            }
        }
        if (!has) return null;
        return new Rectangle(
            (int)MathF.Floor(minX), (int)MathF.Floor(minY),
            (int)MathF.Ceiling(maxX - minX), (int)MathF.Ceiling(maxY - minY));
    }

    /// <summary>The centre of an AABB — the position frame-scene drives the camera to.</summary>
    public static Vector2 Center(Rectangle bounds)
        => new(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);

    /// <summary>
    /// The zoom that fits <paramref name="bounds"/> inside a <paramref name="virtualWidth"/> ×
    /// <paramref name="virtualHeight"/> viewport with a margin (e.g. 0.9 = 10% padding), clamped to
    /// <paramref name="min"/>..<paramref name="max"/>. Degenerate (zero-size) bounds keep the current
    /// zoom unchanged via the caller (here it returns <paramref name="max"/> so a point doesn't zoom to
    /// infinity, but the system only applies fit-zoom when the AABB has area).
    /// </summary>
    public static float FitZoom(Rectangle bounds, int virtualWidth, int virtualHeight, float margin,
        float min, float max)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return Math.Clamp(max, min, max);
        var zx = virtualWidth / (float)bounds.Width;
        var zy = virtualHeight / (float)bounds.Height;
        var fit = MathF.Min(zx, zy) * margin;
        return Math.Clamp(fit, min, max);
    }
}
