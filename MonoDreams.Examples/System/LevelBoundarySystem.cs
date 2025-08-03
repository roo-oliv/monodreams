using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;
using System;
using System.Collections.Generic;

namespace MonoDreams.Examples.System;

[With(typeof(LevelBoundaryComponent))]
public class LevelBoundarySystem(World world) : AEntitySetSystem<GameState>(world)
{
    private const int SplineSegments = 100; // Number of segments to check for boundary intersection

    protected override void Update(GameState state, in Entity entity)
    {
        ref var boundaryComponent = ref entity.Get<LevelBoundaryComponent>();
        ref readonly var spline = ref world.GetEntities().With<CatMulRomSpline>().AsEnumerable().First().Get<CatMulRomSpline>();

        // If there are no boundary polygons, don't do anything
        if (boundaryComponent.BoundaryPolygons.Count == 0)
        {
            return;
        }

        // Clear previous intersection points
        boundaryComponent.IntersectionPoints.Clear();

        // Check if the spline is inside the boundary
        bool isTrackInside = IsSplineInsideBoundary(spline, boundaryComponent);

        // Update the component with the result
        boundaryComponent.IsTrackInsideBoundary = isTrackInside;
        boundaryComponent.TrackEvaluated = true;

        // If track is not inside boundary, update any scoring component as needed
        if (!isTrackInside && entity.Has<VelocityProfileComponent>())
        {
            ref var velocityProfile = ref entity.Get<VelocityProfileComponent>();
            // Set min/max/avg speeds to 0 to indicate an invalid track
            velocityProfile.MinSpeed = 0;
            velocityProfile.MaxSpeed = 0;
            velocityProfile.AverageSpeed = 0;
            velocityProfile.OvertakingOpportunities.Clear();
        }
    }

    private bool IsSplineInsideBoundary(CatMulRomSpline spline, LevelBoundaryComponent boundaryComponent)
    {
        // We'll check each segment of the spline against each boundary line
        var maxProgress = spline.MaxProgress();
        var segmentLength = maxProgress / SplineSegments;

        // Get all the points on the spline for checking
        var splinePoints = new List<Vector2>();
        for (int i = 0; i <= SplineSegments; i++)
        {
            float progress = i * segmentLength;
            splinePoints.Add(spline.GetPoint(progress));
        }

        // Create a list of all boundary edges
        var boundaryEdges = new List<(Vector2, Vector2)>();
        foreach (var polygon in boundaryComponent.BoundaryPolygons)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                boundaryEdges.Add((polygon[i], polygon[(i + 1) % polygon.Length]));
            }
        }

        // Check if any spline segment intersects with any boundary edge
        for (int i = 0; i < splinePoints.Count - 1; i++)
        {
            var splineStart = splinePoints[i];
            var splineEnd = splinePoints[i + 1];

            foreach (var (edgeStart, edgeEnd) in boundaryEdges)
            {
                if (LineSegmentsIntersect(splineStart, splineEnd, edgeStart, edgeEnd, out Vector2 intersection))
                {
                    // Add the intersection point to the list
                    boundaryComponent.IntersectionPoints.Add(intersection);
                    // return false; // Spline intersects boundary
                }
            }
        }

        // Now check if the spline is inside the boundary
        // Take a sample point from the spline and check if it's inside the boundary
        if (splinePoints.Count > 0)
        {
            // Use the first point of the spline for the check
            return IsPointInsideBoundary(splinePoints[0], boundaryComponent.BoundaryPolygons);
        }

        return boundaryComponent.IntersectionPoints.Count == 0;
    }

    private bool IsPointInsideBoundary(Vector2 point, List<Vector2[]> boundaryPolygons)
    {
        // For each polygon in the boundary
        foreach (var polygon in boundaryPolygons)
        {
            if (IsPointInPolygon(point, polygon))
            {
                return true; // If the point is inside any polygon, it's inside the boundary
            }
        }

        return false; // If not inside any polygon, it's outside the boundary
    }

    private bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
    {
        intersection = Vector2.Zero;

        // Calculate the direction vectors
        Vector2 r = p2 - p1;
        Vector2 s = p4 - p3;

        // Calculate the denominator
        float denominator = CrossProduct(r, s);

        // If denominator is 0, lines are parallel or collinear
        if (Math.Abs(denominator) < 0.0001f)
        {
            return false;
        }

        Vector2 qmp = p3 - p1;
        float t = CrossProduct(qmp, s) / denominator;
        float u = CrossProduct(qmp, r) / denominator;

        // Check if the intersection point is within both line segments
        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            // Calculate the intersection point
            intersection = p1 + t * r;
            return true;
        }

        return false;
    }

    private float CrossProduct(Vector2 v1, Vector2 v2)
    {
        return v1.X * v2.Y - v1.Y * v2.X;
    }
}
