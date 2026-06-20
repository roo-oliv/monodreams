using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;

namespace MonoDreams.Demos.UI;

/// Convenience layer over the <see cref="IMeshGenerator"/> primitives for the demos.
/// Two jobs: compose the small set of "styled" shapes every demo screen draws
/// (filled/hollow rectangles, panels = fill + outline, the cursor arrow, checkmarks,
/// crosses, stars), and stamp them onto an entity with the standard
/// <c>Transform + mesh DrawComponent + Visible</c> stack. Centralises the
/// <c>draw.SetMeshData(new XMeshGenerator(...))</c> boilerplate the demos used to repeat.
public static class ShapeBuilder
{
    // ─── composite shape generators ───────────────────────────────────────────

    /// A panel = solid fill behind a thick outline. The minimalist box / frame / checkbox
    /// style (white outline, black fill, etc.).
    public static IMeshGenerator Panel(Rectangle rect, Color fill, Color outline, float thickness)
        => new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(rect, fill))
            .Add(new RectangleOutlineMeshGenerator(rect, thickness, outline));

    /// A mouse-cursor arrow: a notched arrowhead with its tip at the local origin
    /// (the click point), pointing up-left. Filled with <paramref name="fill"/> as two
    /// triangles fanned from the tip (the notch makes it concave, so a centroid fan
    /// won't do) and bordered with a <paramref name="outline"/> outline.
    public static IMeshGenerator Arrow(float size, Color fill, Color outline, float outlineThickness = 1.5f)
    {
        var tip   = new Vector2(0f, 0f);
        var left  = new Vector2(0f, size);
        var notch = new Vector2(0.50f * size, 0.72f * size);
        var right = new Vector2(0.72f * size, 0.50f * size);

        return new CompositeMeshGenerator()
            .Add(new FilledTriangleMeshGenerator(tip, left, notch, fill))
            .Add(new FilledTriangleMeshGenerator(tip, notch, right, fill))
            .Add(new PolygonOutlineMeshGenerator(new[] { tip, left, notch, right }, outlineThickness, outline));
    }

    /// A checkmark stroke fitted inside <paramref name="box"/> (a tick: down to the
    /// low point, then up to the top-right).
    public static IMeshGenerator Checkmark(Rectangle box, float thickness, Color color)
    {
        var p0 = new Vector2(box.X + 0.22f * box.Width, box.Y + 0.52f * box.Height);
        var p1 = new Vector2(box.X + 0.43f * box.Width, box.Y + 0.74f * box.Height);
        var p2 = new Vector2(box.X + 0.78f * box.Width, box.Y + 0.26f * box.Height);
        return new PolylineMeshGenerator(new[] { p0, p1, p2 }, thickness, color);
    }

    /// A plus-shaped cross centred on the local origin (two filled bars). Replaces the
    /// camera demo's old inline CrossMesh helper.
    public static IMeshGenerator Cross(int arm, int thickness, Color color)
    {
        var halfT = thickness / 2;
        return new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(new Rectangle(-arm, -halfT, arm * 2, thickness), color))
            .Add(new FilledRectangleMeshGenerator(new Rectangle(-halfT, -arm, thickness, arm * 2), color));
    }

    /// A filled star centred on <paramref name="center"/> with alternating outer/inner radii.
    public static IMeshGenerator Star(Vector2 center, float outerRadius, float innerRadius, int points, Color color)
    {
        var verts = new Vector2[points * 2];
        for (var i = 0; i < points * 2; i++)
        {
            var radius = (i % 2 == 0) ? outerRadius : innerRadius;
            // Start at the top (-Y) so the star points up.
            var angle = -MathF.PI / 2f + MathF.PI * i / points;
            verts[i] = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }
        return new FilledPolygonMeshGenerator(verts, color);
    }

    // ─── entity stamping ──────────────────────────────────────────────────────

    /// Creates an entity carrying <paramref name="generator"/>'s mesh at
    /// <paramref name="position"/>, ready to render (Transform + mesh DrawComponent + Visible).
    public static Entity Create(World world, IMeshGenerator generator, RenderTargetID target,
        float depth, Vector2 position = default)
    {
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(position));
        var draw = new DrawComponent { Target = target, LayerDepth = depth };
        draw.SetMeshData(generator);
        entity.Set(draw);
        entity.Set<VisibleComponent>();
        return entity;
    }

    /// Filled rectangle entity (bounds in world/local space; position defaults to origin).
    public static Entity Filled(World world, Rectangle rect, Color color, RenderTargetID target, float depth)
        => Create(world, new FilledRectangleMeshGenerator(rect, color), target, depth);

    /// Hollow rectangle (outline) entity.
    public static Entity Outline(World world, Rectangle rect, float thickness, Color color,
        RenderTargetID target, float depth)
        => Create(world, new RectangleOutlineMeshGenerator(rect, thickness, color), target, depth);
}
