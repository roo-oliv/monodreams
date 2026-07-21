#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Boundary;

/// <summary>
/// The pure, allocation-light geometry behind the freeform world boundary (island-authoring
/// plan §5.2) — separated from <c>BoundaryBakeSystem</c> / <c>BoundaryToolSystem</c> /
/// <c>ProxyGeometry</c> so the polyline→segment math is unit-testable without a world, and so
/// there is exactly one source of truth for it.
///
/// <para><b>The bake: one thin convex quad per polyline edge.</b> A coastline is deeply concave;
/// the engine's SAT is convex-only, so a segment chain is the standard robust answer. Each edge
/// (A→B) becomes a rectangle <see cref="EdgeQuads"/> of the boundary's thickness centered on the
/// edge line, wound to match the collision module's convention (positive shoelace sum, like
/// <c>ColliderDefaults.Hexagon</c>). Points are in the boundary entity's LOCAL space (relative to
/// its <c>TransformComponent.Position</c>), and the segment quads stay local — the bake copies the
/// boundary's world position onto each segment child so the root-level collision math places them
/// correctly (see <c>BoundaryBakeSystem</c>).</para>
/// </summary>
public static class BoundaryGeometry
{
    /// <summary>The minimum number of points a boundary keeps (a single segment). Below this there
    /// is no edge to bake and the vertex-delete guard refuses.</summary>
    public const int MinPoints = 2;

    /// <summary>
    /// The convex quad (4 local-space vertices, wound clockwise to the collision-module convention)
    /// for each edge of the open polyline <paramref name="points"/> — N points yield N−1 quads.
    /// Each quad is the edge extruded by ±<paramref name="thickness"/>/2 along the edge normal.
    /// Fewer than <see cref="MinPoints"/> points, a non-positive thickness, or a zero-length edge
    /// contributes nothing (a degenerate edge cannot form a quad).
    /// </summary>
    public static List<Vector2[]> EdgeQuads(IReadOnlyList<Vector2> points, float thickness)
    {
        var quads = new List<Vector2[]>();
        if (points == null || points.Count < MinPoints || thickness <= 0f) return quads;

        var half = thickness / 2f;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var edge = b - a;
            var length = edge.Length();
            if (length <= 1e-4f) continue; // degenerate edge: no quad

            // Unit normal to the edge (perpendicular). Either orientation is fine — EnsureClockwise
            // fixes the winding — so pick the left-hand normal.
            var n = new Vector2(-edge.Y, edge.X) / length;
            var offset = n * half;
            var quad = new[] { a + offset, b + offset, b - offset, a - offset };
            EnsureClockwise(quad);
            quads.Add(quad);
        }

        return quads;
    }

    /// <summary>
    /// Reorders <paramref name="polygon"/> in place to the collision module's winding convention
    /// (positive shoelace sum, matching <c>ColliderDefaults.Hexagon</c> in the y-down world), by
    /// reversing it when its signed area is negative. A degenerate (zero-area) polygon is left as-is.
    /// </summary>
    public static void EnsureClockwise(Vector2[] polygon)
    {
        if (polygon == null || polygon.Length < 3) return;
        if (ShoelaceSum(polygon) < 0f) Array.Reverse(polygon);
    }

    /// <summary>The shoelace (twice-signed-area) sum of a polygon; positive is the module's
    /// clockwise-in-y-down convention.</summary>
    public static float ShoelaceSum(IReadOnlyList<Vector2> polygon)
    {
        var sum = 0f;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum;
    }

    /// <summary>The world-space polyline of <paramref name="localPoints"/> offset by the boundary's
    /// <paramref name="worldPosition"/> (the local→world map — points are local to the entity's
    /// position, no rotation/scale for a boundary).</summary>
    public static Vector2[] WorldPolyline(IReadOnlyList<Vector2> localPoints, Vector2 worldPosition)
    {
        var world = new Vector2[localPoints.Count];
        for (var i = 0; i < localPoints.Count; i++) world[i] = worldPosition + localPoints[i];
        return world;
    }

    /// <summary>The left-hand unit normal of the first polyline edge — the same normal
    /// <see cref="EdgeQuads"/> extrudes the band along — or <c>Vector2.Zero</c> for a
    /// degenerate/too-short polyline.</summary>
    public static Vector2 FirstEdgeNormal(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < MinPoints) return Vector2.Zero;
        var edge = points[1] - points[0];
        var length = edge.Length();
        if (length <= 1e-4f) return Vector2.Zero;
        return new Vector2(-edge.Y, edge.X) / length;
    }

    /// <summary>
    /// The LOCAL-space position of the boundary's <b>thickness handle</b> (island-authoring
    /// Slice 4): the midpoint of the first edge offset by the edge normal × <paramref name="thickness"/>/2,
    /// so it rides the edge of the baked band. Dragging it along the normal widens/narrows the band.
    /// Falls back to the first point for a degenerate polyline.
    /// </summary>
    public static Vector2 ThicknessHandleLocal(IReadOnlyList<Vector2> points, float thickness)
    {
        if (points == null || points.Count == 0) return Vector2.Zero;
        var normal = FirstEdgeNormal(points);
        if (normal == Vector2.Zero) return points[0];
        var mid = (points[0] + points[1]) * 0.5f;
        return mid + normal * (thickness / 2f);
    }

    /// <summary>The arithmetic centroid of a point set (the boundary's pivot at commit).</summary>
    public static Vector2 Centroid(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count == 0) return Vector2.Zero;
        var sum = Vector2.Zero;
        for (var i = 0; i < points.Count; i++) sum += points[i];
        return sum / points.Count;
    }

    /// <summary>
    /// True when <paramref name="point"/> lies within <paramref name="tolerance"/> of the OPEN
    /// polyline <paramref name="polyline"/> (any of its N−1 edge segments — not the closing edge,
    /// since a boundary is not a loop). Used by selection to pick a boundary by clicking its drawn
    /// outline.
    /// </summary>
    public static bool PolylineContains(IReadOnlyList<Vector2> polyline, Vector2 point, float tolerance)
    {
        if (polyline == null || polyline.Count < 2 || tolerance <= 0f) return false;
        var tolSq = tolerance * tolerance;
        for (var i = 0; i < polyline.Count - 1; i++)
            if (DistanceSquaredToSegment(point, polyline[i], polyline[i + 1]) <= tolSq)
                return true;
        return false;
    }

    /// <summary>Squared distance from <paramref name="point"/> to the segment
    /// <paramref name="a"/>–<paramref name="b"/> (degenerate segments collapse to the point test).</summary>
    public static float DistanceSquaredToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSq = ab.LengthSquared();
        if (lengthSq < 1e-12f) return Vector2.DistanceSquared(point, a);
        var t = Vector2.Dot(point - a, ab) / lengthSq;
        t = MathHelper.Clamp(t, 0f, 1f);
        return Vector2.DistanceSquared(point, a + ab * t);
    }
}
