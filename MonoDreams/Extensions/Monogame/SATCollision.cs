using System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;

namespace MonoDreams.Extensions.Monogame;

/// <summary>
/// Static helper for Separating Axis Theorem (SAT) collision detection between convex polygons.
/// Produces the Minimum Translation Vector (MTV) — contact normal and penetration depth.
/// </summary>
public static class SATCollision
{
    /// <summary>
    /// Tests two convex polygons for overlap using SAT.
    /// Returns true if they overlap and outputs the MTV normal (pointing from polyA to polyB)
    /// and penetration depth.
    /// </summary>
    public static bool PolygonVsPolygon(
        ReadOnlySpan<Vector2> polyA,
        ReadOnlySpan<Vector2> polyB,
        out Vector2 normal,
        out float depth)
    {
        normal = Vector2.Zero;
        depth = float.MaxValue;

        // Test axes from polygon A edges
        for (var i = 0; i < polyA.Length; i++)
        {
            var next = (i + 1) % polyA.Length;
            var edge = polyA[next] - polyA[i];
            var axis = new Vector2(-edge.Y, edge.X); // perpendicular

            var lengthSq = axis.LengthSquared();
            if (lengthSq < 1e-12f) continue; // degenerate edge
            axis /= MathF.Sqrt(lengthSq); // normalize

            ProjectPolygon(polyA, axis, out var minA, out var maxA);
            ProjectPolygon(polyB, axis, out var minB, out var maxB);

            if (minA >= maxB || minB >= maxA)
            {
                // Separating axis found — no collision
                depth = 0;
                return false;
            }

            var overlap = MathF.Min(maxA - minB, maxB - minA);
            if (overlap < depth)
            {
                depth = overlap;
                normal = axis;
            }
        }

        // Test axes from polygon B edges
        for (var i = 0; i < polyB.Length; i++)
        {
            var next = (i + 1) % polyB.Length;
            var edge = polyB[next] - polyB[i];
            var axis = new Vector2(-edge.Y, edge.X);

            var lengthSq = axis.LengthSquared();
            if (lengthSq < 1e-12f) continue;
            axis /= MathF.Sqrt(lengthSq);

            ProjectPolygon(polyA, axis, out var minA, out var maxA);
            ProjectPolygon(polyB, axis, out var minB, out var maxB);

            if (minA >= maxB || minB >= maxA)
            {
                depth = 0;
                return false;
            }

            var overlap = MathF.Min(maxA - minB, maxB - minA);
            if (overlap < depth)
            {
                depth = overlap;
                normal = axis;
            }
        }

        // Ensure normal points from A's center toward B's center
        var centerA = PolygonCenter(polyA);
        var centerB = PolygonCenter(polyB);
        var direction = centerB - centerA;
        if (Vector2.Dot(direction, normal) < 0)
        {
            normal = -normal;
        }

        return true;
    }

    /// <summary>
    /// Projects all vertices of a polygon onto an axis, returning min and max scalar values.
    /// </summary>
    public static void ProjectPolygon(ReadOnlySpan<Vector2> polygon, Vector2 axis, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;

        for (var i = 0; i < polygon.Length; i++)
        {
            var projection = Vector2.Dot(polygon[i], axis);
            if (projection < min) min = projection;
            if (projection > max) max = projection;
        }
    }

    /// <summary>
    /// Computes the centroid (average of vertices) of a convex polygon.
    /// </summary>
    public static Vector2 PolygonCenter(ReadOnlySpan<Vector2> polygon)
    {
        var center = Vector2.Zero;
        for (var i = 0; i < polygon.Length; i++)
        {
            center += polygon[i];
        }
        return center / polygon.Length;
    }

    /// <summary>
    /// Converts a BoxCollider + Transform into a 4-vertex polygon in world space,
    /// suitable for SAT testing against ConvexCollider entities.
    /// </summary>
    public static void BoxToPolygon(BoxCollider box, Transform transform, Span<Vector2> output)
    {
        var pos = transform.Position;
        var bounds = box.Bounds;

        output[0] = new Vector2(pos.X + bounds.Left, pos.Y + bounds.Top);
        output[1] = new Vector2(pos.X + bounds.Right, pos.Y + bounds.Top);
        output[2] = new Vector2(pos.X + bounds.Right, pos.Y + bounds.Bottom);
        output[3] = new Vector2(pos.X + bounds.Left, pos.Y + bounds.Bottom);
    }

    /// <summary>
    /// Computes the axis-aligned bounding box encompassing all vertices.
    /// Used as broad-phase rejection before running full SAT.
    /// </summary>
    public static CollisionRect ComputeAABB(ReadOnlySpan<Vector2> vertices)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        for (var i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].X < minX) minX = vertices[i].X;
            if (vertices[i].Y < minY) minY = vertices[i].Y;
            if (vertices[i].X > maxX) maxX = vertices[i].X;
            if (vertices[i].Y > maxY) maxY = vertices[i].Y;
        }

        return new CollisionRect(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }
}
