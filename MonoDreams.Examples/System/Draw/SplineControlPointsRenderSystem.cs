using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(HermiteSpline))]
public class SplineControlPointsRenderSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private const float PointSize = 10f; // Size of control points for selection
    private const float HandleSize = 8f; // Size of handle points
    private const float LineThickness = 1f; // Thickness of lines connecting points and handles
    private static readonly Color PointColor = new(203, 30, 75);
    private static readonly Color TangentColor = new(35, 57, 114);
    private static readonly Color SelectedColor = Color.White;
    

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<HermiteSpline>();

        // Generate visualization for control points and handles
        GenerateControlPointVisualization(entity, spline);
    }

    private void GenerateControlPointVisualization(Entity entity, HermiteSpline spline)
    {
        // Create meshes for control points and handles
        var pointVertices = new List<VertexPositionColor>();
        var pointIndices = new List<int>();

        // Create meshes for lines connecting points and handles
        var lineVertices = new List<VertexPositionColor>();
        var lineIndices = new List<int>();

        var pointIndex = 0;
        var lineIndex = 0;

        // Process control points (vertices)
        for (int i = 0; i < spline.GetAllPoints.Length - 1; i++)
        {
            var point = spline.GetAllPoints[i];
            var pointColor = point.IsSelected ? SelectedColor : PointColor;

            // Add vertex point as a small square
            AddSquare(pointVertices, pointIndices, point.Position, PointSize, pointColor, ref pointIndex);
        }

        // Process tangent points (handles) and connecting lines
        if (spline.GetAllTangents != null)
        {
            for (int i = 0; i < spline.GetAllTangents.Length - 1; i++)
            {
                var tangent = spline.GetAllTangents[i];
                var vertex = spline.GetAllPoints[i];
                var tangentColor = tangent.IsSelected ? SelectedColor : TangentColor;

                // Add tangent handle as a small square
                AddSquare(pointVertices, pointIndices, tangent.Position, HandleSize, tangentColor, ref pointIndex);

                // Add line connecting vertex to handle
                AddLine(lineVertices, lineIndices, vertex.Position, tangent.Position, LineThickness, tangentColor, ref lineIndex);
            }
        }

        // Set or update the control points mesh component
        SetMeshComponent(entity, "ControlPointsMesh", pointVertices.ToArray(), pointIndices.ToArray(), PrimitiveType.TriangleList);

        // Set or update the connecting lines mesh component
        SetMeshComponent(entity, "ControlLinessMesh", lineVertices.ToArray(), lineIndices.ToArray(), PrimitiveType.TriangleList);
    }

    private void AddSquare(List<VertexPositionColor> vertices, List<int> indices, Vector2 position, float size, Color color, ref int indexOffset)
    {
        float halfSize = size / 2;

        // Define four corners of a square
        vertices.Add(new VertexPositionColor(new Vector3(position.X - halfSize, position.Y - halfSize, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(position.X + halfSize, position.Y - halfSize, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(position.X + halfSize, position.Y + halfSize, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(position.X - halfSize, position.Y + halfSize, 0), color));

        // Define two triangles (making a square)
        indices.Add(indexOffset);
        indices.Add(indexOffset + 1);
        indices.Add(indexOffset + 2);

        indices.Add(indexOffset);
        indices.Add(indexOffset + 2);
        indices.Add(indexOffset + 3);

        indexOffset += 4;
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

    private void SetMeshComponent(Entity entity, string componentName, VertexPositionColor[] vertices, int[] indices, PrimitiveType primitiveType)
    {
        // Use the appropriate specialized component
        if (componentName == "ControlPointsMesh")
        {
            if (!entity.Has<ControlPointsDrawComponent>())
            {
                entity.Set(new ControlPointsDrawComponent
                {
                    Type = DrawElementType.Triangles,
                    Vertices = vertices,
                    Indices = indices,
                    PrimitiveType = primitiveType,
                    Target = RenderTargetID.Main,
                    LayerDepth = 0.9f // High layer depth to render on top of the track
                });
            }
            else
            {
                ref var drawComponent = ref entity.Get<ControlPointsDrawComponent>();
                drawComponent.Vertices = vertices;
                drawComponent.Indices = indices;
            }
        }
        else if (componentName == "ControlLinessMesh")
        {
            if (!entity.Has<ControlLinesDrawComponent>())
            {
                entity.Set(new ControlLinesDrawComponent
                {
                    Type = DrawElementType.Triangles,
                    Vertices = vertices,
                    Indices = indices,
                    PrimitiveType = primitiveType,
                    Target = RenderTargetID.Main,
                    LayerDepth = 0.85f // Slightly below control points
                });
            }
            else
            {
                ref var drawComponent = ref entity.Get<ControlLinesDrawComponent>();
                drawComponent.Vertices = vertices;
                drawComponent.Indices = indices;
            }
        }
    }
}
