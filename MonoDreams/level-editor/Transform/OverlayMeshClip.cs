#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Draw;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>
/// Pure triangle-mesh clipping against an axis-aligned rectangle — how the editor overlays
/// respect the inset game viewport. The overlays share the native-resolution Editor render target
/// with the shell chrome: the opaque panels (drawn above the overlay depth band) already cover
/// the reserved margins, but the letterbox/pillarbox bars INSIDE the inset area are bare, and a
/// gizmo line or proxy outline crossing the viewport edge would otherwise draw over them. Every
/// emitted overlay mesh is therefore clipped to <see cref="OverlayProjection.Viewport"/> before
/// it reaches the <c>DrawComponent</c>.
///
/// <para>Sutherland–Hodgman per triangle against the four rectangle half-planes, interpolating
/// vertex color; each clipped convex polygon is fan-triangulated back into the
/// <see cref="PrimitiveType.TriangleList"/> the mesh render path requires (see the rendering
/// premise "IMeshGenerator.Generate() returns a triangle list"). A fully inside mesh is returned
/// as-is (no allocation); a fully outside mesh returns an empty <see cref="MeshData"/>, which
/// <c>DrawComponent.HasValidMesh</c> filters out of the render pass.</para>
/// </summary>
public static class OverlayMeshClip
{
    /// <summary>Clips <paramref name="mesh"/> (a triangle list) to <paramref name="bounds"/>.</summary>
    public static MeshData ClipToRect(in MeshData mesh, Rectangle bounds)
    {
        if (!mesh.IsValid || mesh.PrimitiveType != PrimitiveType.TriangleList) return mesh;
        if (AllInside(mesh.Vertices, bounds)) return mesh;

        float left = bounds.Left, right = bounds.Right, top = bounds.Top, bottom = bounds.Bottom;
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var polygon = new List<VertexPositionColor>(8);
        var clipped = new List<VertexPositionColor>(8);

        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            polygon.Clear();
            polygon.Add(mesh.Vertices[mesh.Indices[i]]);
            polygon.Add(mesh.Vertices[mesh.Indices[i + 1]]);
            polygon.Add(mesh.Vertices[mesh.Indices[i + 2]]);

            ClipEdge(polygon, clipped, v => v.Position.X >= left, (a, b) => LerpAtX(a, b, left));
            (polygon, clipped) = (clipped, polygon);
            if (polygon.Count == 0) continue;
            ClipEdge(polygon, clipped, v => v.Position.X <= right, (a, b) => LerpAtX(a, b, right));
            (polygon, clipped) = (clipped, polygon);
            if (polygon.Count == 0) continue;
            ClipEdge(polygon, clipped, v => v.Position.Y >= top, (a, b) => LerpAtY(a, b, top));
            (polygon, clipped) = (clipped, polygon);
            if (polygon.Count == 0) continue;
            ClipEdge(polygon, clipped, v => v.Position.Y <= bottom, (a, b) => LerpAtY(a, b, bottom));
            (polygon, clipped) = (clipped, polygon);
            if (polygon.Count < 3) continue;

            // Fan-triangulate the clipped convex polygon back into the list.
            var baseIndex = vertices.Count;
            vertices.AddRange(polygon);
            for (var t = 1; t + 1 < polygon.Count; t++)
            {
                indices.Add(baseIndex);
                indices.Add(baseIndex + t);
                indices.Add(baseIndex + t + 1);
            }
        }

        return vertices.Count == 0
            ? new MeshData(Array.Empty<VertexPositionColor>(), Array.Empty<int>())
            : new MeshData(vertices.ToArray(), indices.ToArray());
    }

    private static bool AllInside(VertexPositionColor[] vertices, Rectangle bounds)
    {
        foreach (var v in vertices)
        {
            if (v.Position.X < bounds.Left || v.Position.X > bounds.Right ||
                v.Position.Y < bounds.Top || v.Position.Y > bounds.Bottom)
                return false;
        }
        return true;
    }

    private static void ClipEdge(
        List<VertexPositionColor> input,
        List<VertexPositionColor> output,
        Func<VertexPositionColor, bool> inside,
        Func<VertexPositionColor, VertexPositionColor, VertexPositionColor> intersect)
    {
        output.Clear();
        for (var i = 0; i < input.Count; i++)
        {
            var current = input[i];
            var previous = input[(i + input.Count - 1) % input.Count];
            var currentIn = inside(current);
            var previousIn = inside(previous);

            if (currentIn)
            {
                if (!previousIn) output.Add(intersect(previous, current));
                output.Add(current);
            }
            else if (previousIn)
            {
                output.Add(intersect(previous, current));
            }
        }
    }

    private static VertexPositionColor LerpAtX(VertexPositionColor a, VertexPositionColor b, float x)
    {
        var t = MathHelper.Clamp((x - a.Position.X) / (b.Position.X - a.Position.X), 0f, 1f);
        return Lerp(a, b, t, new Vector3(x, a.Position.Y + (b.Position.Y - a.Position.Y) * t, 0f));
    }

    private static VertexPositionColor LerpAtY(VertexPositionColor a, VertexPositionColor b, float y)
    {
        var t = MathHelper.Clamp((y - a.Position.Y) / (b.Position.Y - a.Position.Y), 0f, 1f);
        return Lerp(a, b, t, new Vector3(a.Position.X + (b.Position.X - a.Position.X) * t, y, 0f));
    }

    private static VertexPositionColor Lerp(VertexPositionColor a, VertexPositionColor b, float t, Vector3 position)
        => new(position, Color.Lerp(a.Color, b.Color, t));
}
