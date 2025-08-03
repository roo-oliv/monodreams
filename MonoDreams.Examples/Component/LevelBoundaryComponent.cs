using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component;

public class LevelBoundaryComponent
{
    // List of convex polygons (triangles) that compose the level boundary
    public List<Vector2[]> BoundaryPolygons { get; set; } = new();

    // The fill color for the area outside the boundary
    public Color OutsideColor { get; set; } = new Color(217, 87, 99, 100); // Semi-transparent red

    // Whether the track has been evaluated against the boundary
    public bool TrackEvaluated { get; set; } = false;

    // Result of the boundary check
    public bool IsTrackInsideBoundary { get; set; } = true;

    // Cache the intersection points if any for visualization
    public List<Vector2> IntersectionPoints { get; set; } = new();
}
