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
/// Generates a dashed rectangle outline. Each edge is broken into evenly spaced
/// dashes of <see cref="DashLength"/> separated by gaps of <see cref="GapLength"/>,
/// drawn with <see cref="Thickness"/> using <see cref="LineMeshGenerator"/> segments.
/// Dash spacing is fitted per-edge so every edge begins and ends on a dash — no
/// half-dash bleeds past a corner.
/// </summary>
public class DashedRectangleOutlineMeshGenerator : IMeshGenerator
{
    public Rectangle Bounds { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; }
    public float DashLength { get; set; }
    public float GapLength { get; set; }

    public DashedRectangleOutlineMeshGenerator(
        Rectangle bounds, float thickness, Color color, float dashLength = 12f, float gapLength = 8f)
    {
        Bounds = bounds;
        Thickness = thickness;
        Color = color;
        DashLength = dashLength;
        GapLength = gapLength;
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

        AddDashedEdge(vertices, indices, topLeft, topRight, ref indexOffset);
        AddDashedEdge(vertices, indices, topRight, bottomRight, ref indexOffset);
        AddDashedEdge(vertices, indices, bottomRight, bottomLeft, ref indexOffset);
        AddDashedEdge(vertices, indices, bottomLeft, topLeft, ref indexOffset);

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }

    private void AddDashedEdge(
        List<VertexPositionColor> vertices, List<int> indices,
        Vector2 start, Vector2 end, ref int indexOffset)
    {
        float length = Vector2.Distance(start, end);
        if (length <= 0f) return;
        Vector2 dir = (end - start) / length;

        // An edge too short for a single dash just gets one solid segment.
        if (length <= DashLength)
        {
            LineMeshGenerator.AddLine(vertices, indices, start, end, Thickness, Color, ref indexOffset);
            return;
        }

        // Fit a whole number of dash+gap periods so the final dash lands exactly on
        // the corner. periods+1 dashes are drawn; the last starts at (length - DashLength).
        float period = DashLength + GapLength;
        int periods = Math.Max(1, (int)MathF.Round((length - DashLength) / period));
        float spacing = (length - DashLength) / periods;

        for (int i = 0; i <= periods; i++)
        {
            float dashStart = i * spacing;
            float dashEnd = Math.Min(dashStart + DashLength, length);
            LineMeshGenerator.AddLine(
                vertices, indices, start + dir * dashStart, start + dir * dashEnd, Thickness, Color, ref indexOffset);
        }
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
