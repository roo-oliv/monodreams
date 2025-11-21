using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Draw;

/// <summary>
/// Interface for procedural mesh generation. Implementations create vertex/index data
/// for various shapes without coupling to game-specific logic.
/// </summary>
public interface IMeshGenerator
{
    MeshData Generate();
}

/// <summary>
/// Generates a filled circle mesh using triangle fan pattern.
/// </summary>
public class CircleMeshGenerator : IMeshGenerator
{
    public Vector2 Center { get; set; }
    public float Radius { get; set; }
    public Color Color { get; set; }
    public int Segments { get; set; } = 16;

    public CircleMeshGenerator(Vector2 center, float radius, Color color, int segments = 16)
    {
        Center = center;
        Radius = radius;
        Color = color;
        Segments = segments;
    }

    public MeshData Generate()
    {
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        AddCircle(vertices, indices, Center, Radius, Color, ref indexOffset, Segments);

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }

    public static void AddCircle(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector2 center,
        float radius,
        Color color,
        ref int indexOffset,
        int segments = 16)
    {
        // Add center vertex
        vertices.Add(new VertexPositionColor(new Vector3(center, 0), color));
        int centerIndex = indexOffset++;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = 2 * MathF.PI * i / segments;
            float angle2 = 2 * MathF.PI * ((i + 1) % segments) / segments;

            var point1 = new Vector2(
                center.X + radius * MathF.Cos(angle1),
                center.Y + radius * MathF.Sin(angle1));

            var point2 = new Vector2(
                center.X + radius * MathF.Cos(angle2),
                center.Y + radius * MathF.Sin(angle2));

            vertices.Add(new VertexPositionColor(new Vector3(point1, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(point2, 0), color));

            indices.Add(centerIndex);
            indices.Add(indexOffset);
            indices.Add(indexOffset + 1);

            indexOffset += 2;
        }
    }
}

/// <summary>
/// Generates a line mesh with configurable thickness.
/// </summary>
public class LineMeshGenerator : IMeshGenerator
{
    public Vector2 Start { get; set; }
    public Vector2 End { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; }

    public LineMeshGenerator(Vector2 start, Vector2 end, float thickness, Color color)
    {
        Start = start;
        End = end;
        Thickness = thickness;
        Color = color;
    }

    public MeshData Generate()
    {
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        AddLine(vertices, indices, Start, End, Thickness, Color, ref indexOffset);

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }

    public static void AddLine(
        List<VertexPositionColor> vertices,
        List<int> indices,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color color,
        ref int indexOffset)
    {
        Vector2 direction = end - start;
        Vector2 perpendicular = new(-direction.Y, direction.X);
        perpendicular.Normalize();
        perpendicular *= thickness / 2;

        vertices.Add(new VertexPositionColor(new Vector3(start + perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(start - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end + perpendicular, 0), color));

        indices.Add(indexOffset);
        indices.Add(indexOffset + 1);
        indices.Add(indexOffset + 2);

        indices.Add(indexOffset);
        indices.Add(indexOffset + 2);
        indices.Add(indexOffset + 3);

        indexOffset += 4;
    }
}

/// <summary>
/// Generates a rectangle outline mesh with configurable thickness.
/// </summary>
public class RectangleOutlineMeshGenerator : IMeshGenerator
{
    public Rectangle Bounds { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; }

    public RectangleOutlineMeshGenerator(Rectangle bounds, float thickness, Color color)
    {
        Bounds = bounds;
        Thickness = thickness;
        Color = color;
    }

    public MeshData Generate()
    {
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        var topLeft = new Vector2(Bounds.Left, Bounds.Top);
        var topRight = new Vector2(Bounds.Right, Bounds.Top);
        var bottomRight = new Vector2(Bounds.Right, Bounds.Bottom);
        var bottomLeft = new Vector2(Bounds.Left, Bounds.Bottom);

        // Top edge
        LineMeshGenerator.AddLine(vertices, indices, topLeft, topRight, Thickness, Color, ref indexOffset);
        // Right edge
        LineMeshGenerator.AddLine(vertices, indices, topRight, bottomRight, Thickness, Color, ref indexOffset);
        // Bottom edge
        LineMeshGenerator.AddLine(vertices, indices, bottomRight, bottomLeft, Thickness, Color, ref indexOffset);
        // Left edge
        LineMeshGenerator.AddLine(vertices, indices, bottomLeft, topLeft, Thickness, Color, ref indexOffset);

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }
}

/// <summary>
/// Generates a filled rectangle mesh.
/// </summary>
public class FilledRectangleMeshGenerator : IMeshGenerator
{
    public Rectangle Bounds { get; set; }
    public Color Color { get; set; }

    public FilledRectangleMeshGenerator(Rectangle bounds, Color color)
    {
        Bounds = bounds;
        Color = color;
    }

    public MeshData Generate()
    {
        var vertices = new VertexPositionColor[]
        {
            new(new Vector3(Bounds.Left, Bounds.Top, 0), Color),
            new(new Vector3(Bounds.Right, Bounds.Top, 0), Color),
            new(new Vector3(Bounds.Right, Bounds.Bottom, 0), Color),
            new(new Vector3(Bounds.Left, Bounds.Bottom, 0), Color)
        };

        var indices = new int[] { 0, 1, 2, 0, 2, 3 };

        return new MeshData(vertices, indices);
    }
}

/// <summary>
/// Generates a gradient mesh along a path with configurable width and color function.
/// </summary>
public class GradientPathMeshGenerator : IMeshGenerator
{
    public Vector2[] PathPoints { get; set; }
    public float Width { get; set; }
    public Func<float, Color> ColorFunction { get; set; }

    public GradientPathMeshGenerator(Vector2[] pathPoints, float width, Func<float, Color> colorFunction)
    {
        PathPoints = pathPoints;
        Width = width;
        ColorFunction = colorFunction;
    }

    public MeshData Generate()
    {
        if (PathPoints == null || PathPoints.Length < 2)
            return new MeshData();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();

        for (int i = 0; i < PathPoints.Length; i++)
        {
            float t = (float)i / (PathPoints.Length - 1);
            var color = ColorFunction(t);

            // Calculate perpendicular for width
            Vector2 direction;
            if (i == 0)
                direction = PathPoints[1] - PathPoints[0];
            else if (i == PathPoints.Length - 1)
                direction = PathPoints[i] - PathPoints[i - 1];
            else
                direction = PathPoints[i + 1] - PathPoints[i - 1];

            direction.Normalize();
            var perpendicular = new Vector2(-direction.Y, direction.X) * (Width / 2);

            vertices.Add(new VertexPositionColor(
                new Vector3(PathPoints[i] + perpendicular, 0), color));
            vertices.Add(new VertexPositionColor(
                new Vector3(PathPoints[i] - perpendicular, 0), color));

            if (i > 0)
            {
                int baseIndex = (i - 1) * 2;
                indices.Add(baseIndex);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 2);

                indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 3);
                indices.Add(baseIndex + 2);
            }
        }

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }
}

/// <summary>
/// Combines multiple mesh generators into a single mesh.
/// </summary>
public class CompositeMeshGenerator : IMeshGenerator
{
    private readonly List<IMeshGenerator> _generators = new();

    public CompositeMeshGenerator Add(IMeshGenerator generator)
    {
        _generators.Add(generator);
        return this;
    }

    public MeshData Generate()
    {
        var allVertices = new List<VertexPositionColor>();
        var allIndices = new List<int>();

        foreach (var generator in _generators)
        {
            var mesh = generator.Generate();
            int indexOffset = allVertices.Count;

            allVertices.AddRange(mesh.Vertices);
            foreach (var index in mesh.Indices)
            {
                allIndices.Add(index + indexOffset);
            }
        }

        return new MeshData(allVertices.ToArray(), allIndices.ToArray());
    }
}
