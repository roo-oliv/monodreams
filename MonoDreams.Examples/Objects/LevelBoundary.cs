using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component;

namespace MonoDreams.Examples.Objects;

public static class LevelBoundary
{
    public static Entity Create(World world)
    {
        var entity = world.CreateEntity();

        // Create a level boundary component
        var boundaryComponent = new LevelBoundaryComponent
        {
            OutsideColor = new Color(6, 10, 23, 180)
        };

        // Example: Create a simple rectangular boundary
        AddRectangularBoundary(boundaryComponent, -1000, -600, 2000, 400);
        AddRectangularBoundary(boundaryComponent, 300, -200, 700, 400);
        AddRectangularBoundary(boundaryComponent, -1000, 200, 2000, 400);
        AddRectangularBoundary(boundaryComponent, -1000, -200, 700, 400);
        AddTriangleBoundary(boundaryComponent, new Vector2(300, 100), new Vector2(300, 200), new Vector2(100, 200));
        AddRectangularBoundary(boundaryComponent, -300, 50, 280, 150);
        AddTreeBoundary(boundaryComponent, 0, 0);
        AddTreeBoundary(boundaryComponent, 24, -6);
        AddTreeBoundary(boundaryComponent, 48, -12);
        AddTreeBoundary(boundaryComponent, 72, -18);
        AddTreeBoundary(boundaryComponent, 12, 8);
        AddTreeBoundary(boundaryComponent, 36, 2);
        AddTreeBoundary(boundaryComponent, 60, 14);
        AddTreeBoundary(boundaryComponent, 84, 6);
        AddTreeBoundary(boundaryComponent, 12, -18);
        AddTreeBoundary(boundaryComponent, 36, -22);

        entity.Set(boundaryComponent);
        entity.Set(new Transform(Vector2.Zero));
        entity.Set(new LevelEntity());

        return entity;
    }
    
    public static Entity Create0(World world)
    {
        var entity = world.CreateEntity();

        // Create a level boundary component
        var boundaryComponent = new LevelBoundaryComponent
        {
            OutsideColor = new Color(6, 10, 23, 180)
        };

        // Example: Create a simple rectangular boundary
        AddRectangularBoundary(boundaryComponent, -1000, -600, 2000, 600);
        AddRectangularBoundary(boundaryComponent, 300, 0, 700, 180);
        AddRectangularBoundary(boundaryComponent, -1000, 180, 2000, 400);
        AddRectangularBoundary(boundaryComponent, -1000, 0, 700, 180);
        AddTreeBoundary(boundaryComponent, 0, 105);
        AddTreeBoundary(boundaryComponent, -15, 110);
        AddTreeBoundary(boundaryComponent, 15, 110);
        AddTreeBoundary(boundaryComponent, 0, 120);
        AddTreeBoundary(boundaryComponent, 30, 100);

        entity.Set(boundaryComponent);
        entity.Set(new Transform(Vector2.Zero));
        entity.Set(new LevelEntity());

        return entity;
    }
    
    public static Entity Create2(World world)
    {
        var entity = world.CreateEntity();

        // Create a level boundary component
        var boundaryComponent = new LevelBoundaryComponent
        {
            OutsideColor = new Color(6, 10, 23, 180)
        };

        // Example: Create a simple rectangular boundary
        AddRectangularBoundary(boundaryComponent, -1000, -600, 2000, 400);
        AddRectangularBoundary(boundaryComponent, 300, -200, 700, 400);
        AddRectangularBoundary(boundaryComponent, -1000, 200, 2000, 400);
        AddRectangularBoundary(boundaryComponent, -1000, -200, 700, 400);
        AddTriangleBoundary(boundaryComponent, new Vector2(-300, -200), new Vector2(-300, 100), new Vector2(-150, -200));
        AddTriangleBoundary(boundaryComponent, new Vector2(-300, 200), new Vector2(-100, 0), new Vector2(-100, 200));
        AddTreeBoundary(boundaryComponent, 0, 0);
        AddTreeBoundary(boundaryComponent, 24, -6);
        AddTreeBoundary(boundaryComponent, 48, -12);
        AddTreeBoundary(boundaryComponent, 72, -18);
        AddTreeBoundary(boundaryComponent, 12, 8);
        AddTreeBoundary(boundaryComponent, 36, 2);
        AddTreeBoundary(boundaryComponent, 60, 14);
        AddTreeBoundary(boundaryComponent, 84, 6);
        AddTreeBoundary(boundaryComponent, 12, -18);
        AddTreeBoundary(boundaryComponent, 36, -22);
        AddTreeBoundary(boundaryComponent, 150, 170);
        AddTreeBoundary(boundaryComponent, 140, 150);
        AddTreeBoundary(boundaryComponent, 160, 150);
        AddTreeBoundary(boundaryComponent, 130, 180);
        AddTreeBoundary(boundaryComponent, 125, 195);
        AddTreeBoundary(boundaryComponent, 115, 185);

        entity.Set(boundaryComponent);
        entity.Set(new Transform(Vector2.Zero));
        entity.Set(new LevelEntity());

        return entity;
    }
    
    private static void AddTreeBoundary(LevelBoundaryComponent boundaryComponent, float x, float y)
    {
        AddTriangleBoundary(boundaryComponent, new Vector2(x - 6, y), new Vector2(x + 6, y), new Vector2(x, y - 18));
        AddRectangularBoundary(boundaryComponent, x - 1, y, 2, 3);
    }

    public static void AddRectangularBoundary(LevelBoundaryComponent boundaryComponent, float x, float y, float width, float height)
    {
        // Define the corners of the rectangle
        var topLeft = new Vector2(x, y);
        var topRight = new Vector2(x + width, y);
        var bottomRight = new Vector2(x + width, y + height);
        var bottomLeft = new Vector2(x, y + height);

        // Break the rectangle into two triangles
        var triangle1 = new[] { topLeft, topRight, bottomLeft };
        var triangle2 = new[] { bottomLeft, topRight, bottomRight };

        // Add the triangles to the boundary component
        boundaryComponent.BoundaryPolygons.Add(triangle1);
        boundaryComponent.BoundaryPolygons.Add(triangle2);
    }

    public static void AddTriangleBoundary(LevelBoundaryComponent boundaryComponent, Vector2 point1, Vector2 point2, Vector2 point3)
    {
        var triangle = new[] { point1, point2, point3 };
        boundaryComponent.BoundaryPolygons.Add(triangle);
    }

    public static void AddCustomBoundary(LevelBoundaryComponent boundaryComponent, params Vector2[] points)
    {
        // For non-convex shapes, we need to triangulate the polygon
        // This is a simple implementation for convex polygons only
        if (points.Length < 3)
            return;

        // Simple fan triangulation from the first point
        // Works for convex polygons but not for general concave polygons
        for (int i = 1; i < points.Length - 1; i++)
        {
            var triangle = new[] { points[0], points[i], points[i + 1] };
            boundaryComponent.BoundaryPolygons.Add(triangle);
        }
    }

    // Create an L-shaped boundary using triangles
    public static void AddLShapedBoundary(LevelBoundaryComponent boundaryComponent, float x, float y, float width, float height, float legWidth, float legHeight)
    {
        // Main rectangle (top part of the L)
        var topLeft = new Vector2(x, y);
        var topRight = new Vector2(x + width, y);
        var bottomRight = new Vector2(x + width, y + legWidth);
        var innerCorner = new Vector2(x + legWidth, y + legWidth);
        var bottomLeft = new Vector2(x, y + height);
        var innerBottom = new Vector2(x + legWidth, y + height);

        // Break the L-shape into triangles
        var triangle1 = new[] { topLeft, topRight, innerCorner };
        var triangle2 = new[] { topLeft, innerCorner, bottomLeft };
        var triangle3 = new[] { bottomLeft, innerCorner, innerBottom };

        // Add the triangles to the boundary component
        boundaryComponent.BoundaryPolygons.Add(triangle1);
        boundaryComponent.BoundaryPolygons.Add(triangle2);
        boundaryComponent.BoundaryPolygons.Add(triangle3);
    }
}
