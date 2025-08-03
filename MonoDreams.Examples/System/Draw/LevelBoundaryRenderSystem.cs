using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(LevelBoundaryComponent))]
public class LevelBoundaryRenderSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private static readonly Color IntersectionColor = Color.Red;
    private const float IntersectionPointSize = 5f;

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var boundaryComponent = ref entity.Get<LevelBoundaryComponent>();

        if (boundaryComponent.BoundaryPolygons.Count == 0)
        {
            ClearVisualizations(entity);
            return;
        }

        // Generate visualization for the boundary
        GenerateBoundaryVisualization(entity, boundaryComponent);
    }

    private void GenerateBoundaryVisualization(Entity entity, LevelBoundaryComponent boundaryComponent)
    {
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var indexOffset = 0;

        // First, generate triangles for the outside area (using the outside color)
        // We'll create a big rectangle covering the visible area
        // and then cut out the boundary shapes from it
        GenerateOutsideArea(vertices, indices, boundaryComponent, ref indexOffset);

        // Next, generate line borders for each boundary polygon
        foreach (var polygon in boundaryComponent.BoundaryPolygons)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                var start = polygon[i];
                var end = polygon[(i + 1) % polygon.Length];

                // Draw boundary lines
                // AddLine(vertices, indices, start, end, BorderThickness, BorderColor, ref indexOffset);
            }
        }

        // Add intersection points visualization if any
        if (!boundaryComponent.IsTrackInsideBoundary)
        {
            foreach (var point in boundaryComponent.IntersectionPoints)
            {
                AddXMark(vertices, indices, point, IntersectionPointSize, IntersectionColor, ref indexOffset);
            }
        }

        // Set or update the boundary mesh component
        SetMeshComponent(entity, vertices.ToArray(), indices.ToArray(), PrimitiveType.TriangleList, boundaryComponent.IsTrackInsideBoundary);
    }

    private void GenerateOutsideArea(List<VertexPositionColor> vertices, List<int> indices, LevelBoundaryComponent boundaryComponent, ref int indexOffset)
    {
        // We'll create filled triangles for each of the boundary polygons
        // Each polygon is already a convex shape (triangle) in our case
        foreach (var polygon in boundaryComponent.BoundaryPolygons)
        {
            if (polygon.Length < 3) continue;

            // For each polygon, create a filled shape with the outside color
            // We use a different approach than outside area because we're composing the boundary from convex shapes
            for (int i = 1; i < polygon.Length - 1; i++)
            {
                // Create a triangle fan from the first point
                // Add the three points of the triangle
                vertices.Add(new VertexPositionColor(new Vector3(polygon[0], 0), boundaryComponent.OutsideColor));
                vertices.Add(new VertexPositionColor(new Vector3(polygon[i], 0), boundaryComponent.OutsideColor));
                vertices.Add(new VertexPositionColor(new Vector3(polygon[i + 1], 0), boundaryComponent.OutsideColor));

                // Add indices for the triangle
                indices.Add(indexOffset);
                indices.Add(indexOffset + 1);
                indices.Add(indexOffset + 2);

                indexOffset += 3;
            }
        }
    }

    private void AddLine(List<VertexPositionColor> vertices, List<int> indices, Vector2 start, Vector2 end, float thickness, Color color, ref int indexOffset)
    {
        // Calculate direction and perpendicular vector for thickness
        Vector2 direction = end - start;
        Vector2 perpendicular = new Vector2(-direction.Y, direction.X);
        perpendicular.Normalize();
        perpendicular *= thickness / 2;

        // Create the four corners of the line segment
        vertices.Add(new VertexPositionColor(new Vector3(start + perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(start - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end + perpendicular, 0), color));

        // Create two triangles to form the line
        indices.Add(indexOffset);
        indices.Add(indexOffset + 1);
        indices.Add(indexOffset + 2);

        indices.Add(indexOffset);
        indices.Add(indexOffset + 2);
        indices.Add(indexOffset + 3);

        indexOffset += 4;
    }

    private void AddXMark(List<VertexPositionColor> vertices, List<int> indices, Vector2 position, float size, Color color, ref int indexOffset)
    {
        float halfSize = size / 2;
        float lineThickness = 2f;

        // Create two lines that form an X
        // First line: top-left to bottom-right
        AddLine(vertices, indices, 
            new Vector2(position.X - halfSize, position.Y - halfSize),
            new Vector2(position.X + halfSize, position.Y + halfSize),
            lineThickness, color, ref indexOffset);

        // Second line: top-right to bottom-left
        AddLine(vertices, indices, 
            new Vector2(position.X + halfSize, position.Y - halfSize),
            new Vector2(position.X - halfSize, position.Y + halfSize),
            lineThickness, color, ref indexOffset);
    }

    private void AddSquare(List<VertexPositionColor> vertices, List<int> indices, Vector2 position, float size, Color color, ref int indexOffset)
    {
        float halfSize = size / 2;

        // Define the four corners of the square
        var topLeft = new Vector2(position.X - halfSize, position.Y - halfSize);
        var topRight = new Vector2(position.X + halfSize, position.Y - halfSize);
        var bottomRight = new Vector2(position.X + halfSize, position.Y + halfSize);
        var bottomLeft = new Vector2(position.X - halfSize, position.Y + halfSize);

        // Add vertices
        vertices.Add(new VertexPositionColor(new Vector3(topLeft, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(topRight, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(bottomRight, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(bottomLeft, 0), color));

        // Add indices for two triangles
        indices.Add(indexOffset);
        indices.Add(indexOffset + 1);
        indices.Add(indexOffset + 2);

        indices.Add(indexOffset);
        indices.Add(indexOffset + 2);
        indices.Add(indexOffset + 3);

        indexOffset += 4;
    }

    private void SetMeshComponent(Entity entity, VertexPositionColor[] vertices, int[] indices, PrimitiveType primitiveType, bool isTrackInsideBoundary)
    {
        if (!entity.Has<BoundaryDrawComponent>())
        {
            entity.Set(new BoundaryDrawComponent
            {
                Type = DrawElementType.Triangles,
                Vertices = vertices,
                Indices = indices,
                PrimitiveType = primitiveType,
                Target = RenderTargetID.Main,
                LayerDepth = 0.1f, // Below the track
                IsTrackInsideBoundary = isTrackInsideBoundary
            });
        }
        else
        {
            ref var drawComponent = ref entity.Get<BoundaryDrawComponent>();
            drawComponent.Vertices = vertices;
            drawComponent.Indices = indices;
            drawComponent.IsTrackInsideBoundary = isTrackInsideBoundary;
        }
    }

    private void ClearVisualizations(in Entity entity)
    {
        if (entity.Has<BoundaryDrawComponent>())
        {
            entity.Remove<BoundaryDrawComponent>();
        }
    }
}
