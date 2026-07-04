#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Proxy;

/// <summary>Which resize handle of a box collider proxy a drag grabbed. Four corners resize
/// both axes; four edge midpoints resize one. <see cref="None"/> = not a resize drag (the
/// centre move handle / no handle).</summary>
public enum BoxResizeHandle
{
    None = 0,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// The pure geometry behind the box collider proxy's <b>resize handles</b> (island-authoring
/// Slice 2): eight handles on the box's world rectangle — corners + edge midpoints — hit-tested
/// analytically like every gizmo handle, each adjusting the grabbed edge(s) of the
/// Transform-relative <c>BoxColliderComponent.Bounds</c>. Separated from <c>GizmoSystem</c>
/// (which owns only the drag lifecycle) so the edge math is unit-testable without a world or a
/// cursor. The box rect is axis-aligned in world space (<c>WorldPosition + Bounds</c>), so a
/// world-space drag delta applies to the local <c>Bounds</c> unchanged.
/// </summary>
public static class BoxResize
{
    /// <summary>The minimum box side length a resize can shrink to (whole world units — Bounds
    /// is an int rectangle). The opposite edge is the anchor: dragging past it clamps here
    /// instead of inverting the box.</summary>
    public const int MinSize = 1;

    private static readonly BoxResizeHandle[] All =
    {
        BoxResizeHandle.TopLeft, BoxResizeHandle.Top, BoxResizeHandle.TopRight,
        BoxResizeHandle.Right, BoxResizeHandle.BottomRight, BoxResizeHandle.Bottom,
        BoxResizeHandle.BottomLeft, BoxResizeHandle.Left,
    };

    /// <summary>Every handle in a stable order (TL, T, TR, R, BR, B, BL, L) — the visual
    /// emission and hit-test iterate the same list.</summary>
    public static ReadOnlySpan<BoxResizeHandle> Handles => All;

    /// <summary>The world-space point of <paramref name="handle"/> on the box rect
    /// <paramref name="min"/>–<paramref name="max"/> (corner or edge midpoint).</summary>
    public static Vector2 HandleWorld(Vector2 min, Vector2 max, BoxResizeHandle handle)
    {
        var cx = (min.X + max.X) / 2f;
        var cy = (min.Y + max.Y) / 2f;
        return handle switch
        {
            BoxResizeHandle.TopLeft => new Vector2(min.X, min.Y),
            BoxResizeHandle.Top => new Vector2(cx, min.Y),
            BoxResizeHandle.TopRight => new Vector2(max.X, min.Y),
            BoxResizeHandle.Right => new Vector2(max.X, cy),
            BoxResizeHandle.BottomRight => new Vector2(max.X, max.Y),
            BoxResizeHandle.Bottom => new Vector2(cx, max.Y),
            BoxResizeHandle.BottomLeft => new Vector2(min.X, max.Y),
            BoxResizeHandle.Left => new Vector2(min.X, cy),
            _ => new Vector2(cx, cy),
        };
    }

    /// <summary>The handle under <paramref name="cursor"/> within <paramref name="radius"/>
    /// (world units — the caller scales a pixel constant by <c>1/Zoom</c>), or
    /// <see cref="BoxResizeHandle.None"/>. Nearest wins when several are within range (a tiny
    /// box packs its handles close together).</summary>
    public static BoxResizeHandle HitTest(Vector2 min, Vector2 max, Vector2 cursor, float radius)
    {
        var best = BoxResizeHandle.None;
        var bestDistSq = radius * radius;
        foreach (var handle in All)
        {
            var distSq = Vector2.DistanceSquared(cursor, HandleWorld(min, max, handle));
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = handle;
            }
        }
        return best;
    }

    /// <summary>
    /// The resized <c>Bounds</c>: <paramref name="worldDelta"/> (rounded to whole units — Bounds
    /// is an int rectangle) moves the edge(s) <paramref name="handle"/> grabs, the opposite
    /// edge(s) stay anchored, and each side clamps at <see cref="MinSize"/> so the box can never
    /// invert or vanish.
    /// </summary>
    public static Rectangle Apply(Rectangle before, BoxResizeHandle handle, Vector2 worldDelta)
    {
        var dx = (int)MathF.Round(worldDelta.X);
        var dy = (int)MathF.Round(worldDelta.Y);
        int left = before.Left, top = before.Top, right = before.Right, bottom = before.Bottom;

        switch (handle)
        {
            case BoxResizeHandle.TopLeft:
            case BoxResizeHandle.Left:
            case BoxResizeHandle.BottomLeft:
                left = Math.Min(right - MinSize, left + dx);
                break;
        }
        switch (handle)
        {
            case BoxResizeHandle.TopRight:
            case BoxResizeHandle.Right:
            case BoxResizeHandle.BottomRight:
                right = Math.Max(left + MinSize, right + dx);
                break;
        }
        switch (handle)
        {
            case BoxResizeHandle.TopLeft:
            case BoxResizeHandle.Top:
            case BoxResizeHandle.TopRight:
                top = Math.Min(bottom - MinSize, top + dy);
                break;
        }
        switch (handle)
        {
            case BoxResizeHandle.BottomLeft:
            case BoxResizeHandle.Bottom:
            case BoxResizeHandle.BottomRight:
                bottom = Math.Max(top + MinSize, bottom + dy);
                break;
        }

        return new Rectangle(left, top, right - left, bottom - top);
    }
}
