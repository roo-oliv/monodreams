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
/// Generates a hollow circle (ring) outline mesh — the circular analog of
/// <see cref="RectangleOutlineMeshGenerator"/>. The border is built from
/// <see cref="Segments"/> thick line quads connecting evenly spaced points around
/// the circumference, so the result is a triangle list just like the other
/// generators.
/// </summary>
public class CircleOutlineMeshGenerator : IMeshGenerator
{
    public Vector2 Center { get; set; }
    public float Radius { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; }
    public int Segments { get; set; } = 24;

    public CircleOutlineMeshGenerator(Vector2 center, float radius, float thickness, Color color, int segments = 24)
    {
        Center = center;
        Radius = radius;
        Thickness = thickness;
        Color = color;
        Segments = segments;
    }

    public MeshData Generate()
    {
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        for (int i = 0; i < Segments; i++)
        {
            float angle1 = 2 * MathF.PI * i / Segments;
            float angle2 = 2 * MathF.PI * ((i + 1) % Segments) / Segments;

            var point1 = new Vector2(
                Center.X + Radius * MathF.Cos(angle1),
                Center.Y + Radius * MathF.Sin(angle1));
            var point2 = new Vector2(
                Center.X + Radius * MathF.Cos(angle2),
                Center.Y + Radius * MathF.Sin(angle2));

            LineMeshGenerator.AddLine(vertices, indices, point1, point2, Thickness, Color, ref indexOffset);
        }

        return new MeshData(vertices.ToArray(), indices.ToArray());
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
/// Generates a filled rounded rectangle: straight edges with quarter-circle corner arcs.
/// The outline is the rectangle inset by <see cref="Radius"/> on each corner, with each
/// corner replaced by a <see cref="CornerSegments"/>-segment arc; that closed point loop is
/// then triangulated by fanning from the centre (the rect's centroid "sees" every edge, so the
/// same centroid-fan <see cref="FilledPolygonMeshGenerator"/> uses is valid). A radius of zero
/// degenerates to a plain filled rectangle. Used for borderless speech-balloon bodies.
/// </summary>
public class FilledRoundedRectangleMeshGenerator : IMeshGenerator
{
    public Rectangle Bounds { get; set; }
    public float Radius { get; set; }
    public Color Color { get; set; }
    public int CornerSegments { get; set; } = 5;

    public FilledRoundedRectangleMeshGenerator(Rectangle bounds, float radius, Color color, int cornerSegments = 5)
    {
        Bounds = bounds;
        Radius = radius;
        Color = color;
        CornerSegments = Math.Max(1, cornerSegments);
    }

    public MeshData Generate()
    {
        // Clamp the radius so two opposite corners never overlap.
        var r = MathHelper.Clamp(Radius, 0f, MathF.Min(Bounds.Width, Bounds.Height) * 0.5f);
        if (r <= 0f)
            return new FilledRectangleMeshGenerator(Bounds, Color).Generate();

        float l = Bounds.Left, t = Bounds.Top, right = Bounds.Right, b = Bounds.Bottom;

        // Corner arc centres (inset by r), each swept a quarter-turn. Order them so the
        // emitted boundary points walk the perimeter clockwise: TL → TR → BR → BL.
        var corners = new (Vector2 center, float start)[]
        {
            (new Vector2(l + r,     t + r),     MathF.PI),            // top-left:     180°→270°
            (new Vector2(right - r, t + r),     MathF.PI * 1.5f),     // top-right:    270°→360°
            (new Vector2(right - r, b - r),     0f),                  // bottom-right: 0°→90°
            (new Vector2(l + r,     b - r),     MathF.PI * 0.5f),     // bottom-left:  90°→180°
        };

        var points = new List<Vector2>();
        foreach (var (center, start) in corners)
        {
            for (var i = 0; i <= CornerSegments; i++)
            {
                var a = start + (MathF.PI * 0.5f) * i / CornerSegments;
                points.Add(new Vector2(center.X + r * MathF.Cos(a), center.Y + r * MathF.Sin(a)));
            }
        }

        return new FilledPolygonMeshGenerator(points.ToArray(), Color).Generate();
    }
}

/// <summary>
/// Generates a single filled triangle from three points. The renderer draws meshes
/// with <see cref="Microsoft.Xna.Framework.Graphics.RasterizerState.CullNone"/>, so the
/// winding order of the three points does not matter.
/// </summary>
public class FilledTriangleMeshGenerator : IMeshGenerator
{
    public Vector2 A { get; set; }
    public Vector2 B { get; set; }
    public Vector2 C { get; set; }
    public Color Color { get; set; }

    public FilledTriangleMeshGenerator(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        A = a;
        B = b;
        C = c;
        Color = color;
    }

    public MeshData Generate()
    {
        var vertices = new[]
        {
            new VertexPositionColor(new Vector3(A, 0), Color),
            new VertexPositionColor(new Vector3(B, 0), Color),
            new VertexPositionColor(new Vector3(C, 0), Color),
        };
        return new MeshData(vertices, new[] { 0, 1, 2 });
    }
}

/// <summary>
/// Generates a filled convex (or star-convex) polygon by fanning triangles out from the
/// point set's centroid — the polygonal analog of <see cref="CircleMeshGenerator"/>. Works
/// for any shape whose centroid "sees" every edge (convex polygons and regular stars).
/// </summary>
public class FilledPolygonMeshGenerator : IMeshGenerator
{
    public Vector2[] Points { get; set; }
    public Color Color { get; set; }

    public FilledPolygonMeshGenerator(Vector2[] points, Color color)
    {
        Points = points;
        Color = color;
    }

    public MeshData Generate()
    {
        if (Points == null || Points.Length < 3) return new MeshData();

        var centroid = Vector2.Zero;
        foreach (var p in Points) centroid += p;
        centroid /= Points.Length;

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();

        vertices.Add(new VertexPositionColor(new Vector3(centroid, 0), Color));
        for (var i = 0; i < Points.Length; i++)
            vertices.Add(new VertexPositionColor(new Vector3(Points[i], 0), Color));

        for (var i = 0; i < Points.Length; i++)
        {
            indices.Add(0);
            indices.Add(1 + i);
            indices.Add(1 + (i + 1) % Points.Length);
        }

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }
}

/// <summary>
/// Generates a thick outline around an arbitrary point loop — the general case of
/// <see cref="RectangleOutlineMeshGenerator"/> and <see cref="CircleOutlineMeshGenerator"/>.
/// Each consecutive pair of points becomes a thick line quad; with <see cref="Closed"/> the
/// last point connects back to the first.
/// </summary>
public class PolygonOutlineMeshGenerator : IMeshGenerator
{
    public Vector2[] Points { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; }
    public bool Closed { get; set; }

    public PolygonOutlineMeshGenerator(Vector2[] points, float thickness, Color color, bool closed = true)
    {
        Points = points;
        Thickness = thickness;
        Color = color;
        Closed = closed;
    }

    public MeshData Generate()
    {
        if (Points == null || Points.Length < 2) return new MeshData();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var indexOffset = 0;

        var segments = Closed ? Points.Length : Points.Length - 1;
        for (var i = 0; i < segments; i++)
        {
            var start = Points[i];
            var end = Points[(i + 1) % Points.Length];
            LineMeshGenerator.AddLine(vertices, indices, start, end, Thickness, Color, ref indexOffset);
        }

        return new MeshData(vertices.ToArray(), indices.ToArray());
    }
}

/// <summary>
/// Generates an open thick path through a list of points (a polyline) — e.g. a checkmark or
/// a mouth curve. The single-segment case is <see cref="LineMeshGenerator"/>.
/// </summary>
public class PolylineMeshGenerator : IMeshGenerator
{
    public Vector2[] Points { get; set; }
    public float Thickness { get; set; }
    public Color Color { get; set; }

    public PolylineMeshGenerator(Vector2[] points, float thickness, Color color)
    {
        Points = points;
        Thickness = thickness;
        Color = color;
    }

    public MeshData Generate()
    {
        if (Points == null || Points.Length < 2) return new MeshData();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var indexOffset = 0;

        for (var i = 0; i < Points.Length - 1; i++)
            LineMeshGenerator.AddLine(vertices, indices, Points[i], Points[i + 1], Thickness, Color, ref indexOffset);

        return new MeshData(vertices.ToArray(), indices.ToArray());
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
