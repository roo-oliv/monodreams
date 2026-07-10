using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Extensions.Monogame;
using Xunit;

namespace MonoDreams.Tests.Collision;

public class SATCollisionTests
{
    // Helper: unit square centered at origin
    private static Vector2[] UnitSquare(float cx = 0, float cy = 0) =>
    [
        new(cx - 0.5f, cy - 0.5f),
        new(cx + 0.5f, cy - 0.5f),
        new(cx + 0.5f, cy + 0.5f),
        new(cx - 0.5f, cy + 0.5f),
    ];

    // Helper: equilateral-ish triangle pointing up
    private static Vector2[] Triangle(float cx = 0, float cy = 0) =>
    [
        new(cx, cy - 1f),        // top
        new(cx + 1f, cy + 1f),   // bottom-right
        new(cx - 1f, cy + 1f),   // bottom-left
    ];

    [Fact]
    public void SeparatedSquares_NoCollision()
    {
        var a = UnitSquare(0, 0);
        var b = UnitSquare(5, 0);

        var result = SATCollision.PolygonVsPolygon(a, b, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void OverlappingSquares_ReturnsCollision()
    {
        var a = UnitSquare(0, 0);
        var b = UnitSquare(0.5f, 0); // overlaps by 0.5 on X

        var result = SATCollision.PolygonVsPolygon(a, b, out var normal, out var depth);

        Assert.True(result);
        Assert.True(depth > 0);
        // Normal should point from A toward B (positive X direction)
        Assert.True(normal.X > 0 || normal.Y != 0, "Normal should have a component pointing from A to B");
    }

    [Fact]
    public void OverlappingSquares_CorrectMTV()
    {
        var a = UnitSquare(0, 0);
        var b = UnitSquare(0.8f, 0); // overlap = 1.0 - 0.8 = 0.2

        var result = SATCollision.PolygonVsPolygon(a, b, out var normal, out var depth);

        Assert.True(result);
        Assert.Equal(0.2f, depth, 3);
        // Normal should point from A to B (positive X)
        Assert.True(normal.X > 0);
    }

    [Fact]
    public void TriangleVsSquare_Overlapping()
    {
        var triangle = Triangle(0, 0);
        var square = UnitSquare(0.5f, 0.5f);

        var result = SATCollision.PolygonVsPolygon(triangle, square, out var normal, out var depth);

        Assert.True(result);
        Assert.True(depth > 0);
    }

    [Fact]
    public void TriangleVsSquare_Separated()
    {
        var triangle = Triangle(0, 0);
        var square = UnitSquare(10, 10);

        var result = SATCollision.PolygonVsPolygon(triangle, square, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void RotatedRectangle_VsAxisAligned()
    {
        // 45-degree rotated square (diamond shape) at origin
        var cos45 = MathF.Cos(MathF.PI / 4f);
        var diamond = new Vector2[]
        {
            new(0, -cos45),    // top
            new(cos45, 0),     // right
            new(0, cos45),     // bottom
            new(-cos45, 0),    // left
        };

        // Axis-aligned square nearby — should overlap
        var square = UnitSquare(0.5f, 0);

        var result = SATCollision.PolygonVsPolygon(diamond, square, out var normal, out var depth);

        Assert.True(result);
        Assert.True(depth > 0);
    }

    [Fact]
    public void BoxToPolygon_MatchesAABB()
    {
        var box = new BoxColliderComponent(new Rectangle(-10, -10, 20, 20));
        var transform = new TransformComponent(new Vector2(100, 200));
        var output = new Vector2[4];

        SATCollision.BoxToPolygon(box, transform, output);

        // Expected: bounds (-10,-10)+(100,200) = (90,190) to (110,210)
        Assert.Equal(new Vector2(90, 190), output[0]);  // top-left
        Assert.Equal(new Vector2(110, 190), output[1]); // top-right
        Assert.Equal(new Vector2(110, 210), output[2]); // bottom-right
        Assert.Equal(new Vector2(90, 210), output[3]);   // bottom-left
    }

    [Fact]
    public void BoxToPolygon_SATMatchesIntersection()
    {
        // Two overlapping boxes — SAT on converted polygons should agree with AABB intersection
        var boxA = new BoxColliderComponent(new Rectangle(0, 0, 20, 20));
        var transformA = new TransformComponent(new Vector2(0, 0));
        var polyA = new Vector2[4];
        SATCollision.BoxToPolygon(boxA, transformA, polyA);

        var boxB = new BoxColliderComponent(new Rectangle(0, 0, 20, 20));
        var transformB = new TransformComponent(new Vector2(15, 15));
        var polyB = new Vector2[4];
        SATCollision.BoxToPolygon(boxB, transformB, polyB);

        var aabbA = CollisionRect.FromBounds(boxA.Bounds, transformA.Position);
        var aabbB = CollisionRect.FromBounds(boxB.Bounds, transformB.Position);
        var aabbIntersects = aabbA.Intersects(aabbB);

        var satResult = SATCollision.PolygonVsPolygon(polyA, polyB, out _, out _);

        Assert.Equal(aabbIntersects, satResult);
    }

    [Fact]
    public void Containment_FullyInside()
    {
        // Small square fully inside a large square
        var large = UnitSquare(0, 0);
        var small = new Vector2[]
        {
            new(-0.1f, -0.1f),
            new(0.1f, -0.1f),
            new(0.1f, 0.1f),
            new(-0.1f, 0.1f),
        };

        var result = SATCollision.PolygonVsPolygon(large, small, out _, out var depth);

        Assert.True(result);
        Assert.True(depth > 0);
    }

    [Fact]
    public void EdgeTouching_NoCollision()
    {
        // Two squares sharing an edge (no overlap, just touching)
        var a = UnitSquare(0, 0);
        var b = UnitSquare(1.0f, 0); // right edge of A touches left edge of B exactly

        var result = SATCollision.PolygonVsPolygon(a, b, out _, out _);

        // SAT with strict inequality: touching = no overlap
        Assert.False(result);
    }

    [Fact]
    public void ProjectPolygon_CorrectRange()
    {
        var square = UnitSquare(0, 0);
        var axis = new Vector2(1, 0); // X axis

        SATCollision.ProjectPolygon(square, axis, out var min, out var max);

        Assert.Equal(-0.5f, min, 3);
        Assert.Equal(0.5f, max, 3);
    }

    [Fact]
    public void PolygonCenter_IsAverage()
    {
        var square = UnitSquare(2, 3);
        var center = SATCollision.PolygonCenter(square);

        Assert.Equal(2f, center.X, 3);
        Assert.Equal(3f, center.Y, 3);
    }

    [Fact]
    public void ComputeAABB_CorrectBounds()
    {
        var triangle = Triangle(5, 5);
        var aabb = SATCollision.ComputeAABB(triangle);

        Assert.Equal(4f, aabb.Left, 3);   // 5 - 1
        Assert.Equal(4f, aabb.Top, 3);    // 5 - 1
        Assert.Equal(6f, aabb.Right, 3);  // 5 + 1
        Assert.Equal(6f, aabb.Bottom, 3); // 5 + 1
    }

    [Fact]
    public void NormalDirection_PointsFromAToB()
    {
        var a = UnitSquare(0, 0);
        var b = UnitSquare(0.5f, 0); // B is to the right of A

        SATCollision.PolygonVsPolygon(a, b, out var normal, out _);

        // Normal should point from A toward B → positive X
        Assert.True(normal.X > 0);
        Assert.Equal(0f, normal.Y, 3);
    }

    [Fact]
    public void Pentagon_VsSquare()
    {
        // Regular pentagon centered at origin
        var pentagon = new Vector2[5];
        for (var i = 0; i < 5; i++)
        {
            var angle = MathF.PI * 2f * i / 5f - MathF.PI / 2f;
            pentagon[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        // Overlapping square
        var square = UnitSquare(0.5f, 0);

        var result = SATCollision.PolygonVsPolygon(pentagon, square, out _, out var depth);

        Assert.True(result);
        Assert.True(depth > 0);
    }

    [Fact]
    public void ConvexCollider_UpdateWorldVertices()
    {
        var model = new Vector2[]
        {
            new(-1, -1),
            new(1, -1),
            new(1, 1),
            new(-1, 1),
        };
        var collider = new ConvexColliderComponent(model);
        var transform = new TransformComponent(new Vector2(10, 20));

        collider.UpdateWorldVertices(transform);

        // With no rotation/scale, world = model + position
        Assert.Equal(new Vector2(9, 19), collider.WorldVertices[0]);
        Assert.Equal(new Vector2(11, 19), collider.WorldVertices[1]);
        Assert.Equal(new Vector2(11, 21), collider.WorldVertices[2]);
        Assert.Equal(new Vector2(9, 21), collider.WorldVertices[3]);
    }

    [Fact]
    public void ConvexCollider_UpdateWorldVertices_WithRotation()
    {
        var model = new Vector2[]
        {
            new(1, 0),
            new(0, 1),
            new(-1, 0),
        };
        var collider = new ConvexColliderComponent(model);
        // 90 degrees rotation
        var transform = new TransformComponent(new Vector2(0, 0), MathF.PI / 2f);

        collider.UpdateWorldVertices(transform);

        // After 90° CCW: (1,0)→(0,1), (0,1)→(-1,0), (-1,0)→(0,-1)
        Assert.Equal(0f, collider.WorldVertices[0].X, 3);
        Assert.Equal(1f, collider.WorldVertices[0].Y, 3);
        Assert.Equal(-1f, collider.WorldVertices[1].X, 3);
        Assert.Equal(0f, collider.WorldVertices[1].Y, 3);
        Assert.Equal(0f, collider.WorldVertices[2].X, 3);
        Assert.Equal(-1f, collider.WorldVertices[2].Y, 3);
    }

    [Fact]
    public void ConvexCollider_UpdateWorldVertices_WithScale()
    {
        var model = new Vector2[]
        {
            new(-1, -1),
            new(1, -1),
            new(0, 1),
        };
        var collider = new ConvexColliderComponent(model);
        var transform = new TransformComponent(new Vector2(0, 0), scale: new Vector2(2, 3));

        collider.UpdateWorldVertices(transform);

        Assert.Equal(new Vector2(-2, -3), collider.WorldVertices[0]);
        Assert.Equal(new Vector2(2, -3), collider.WorldVertices[1]);
        Assert.Equal(new Vector2(0, 3), collider.WorldVertices[2]);
    }

    [Fact]
    public void ConvexCollider_IgnoreTransformRotation_DoesNotRotate()
    {
        var model = new Vector2[]
        {
            new(1, 0),
            new(0, 1),
            new(-1, 0),
        };
        var collider = new ConvexColliderComponent(model, ignoreTransformRotation: true);
        // Set a non-zero rotation that would normally rotate vertices
        var transform = new TransformComponent(new Vector2(5, 10), MathF.PI / 2f);

        collider.UpdateWorldVertices(transform);

        // Rotation should be ignored — world = model + position (no rotation applied)
        Assert.Equal(6f, collider.WorldVertices[0].X, 3);
        Assert.Equal(10f, collider.WorldVertices[0].Y, 3);
        Assert.Equal(5f, collider.WorldVertices[1].X, 3);
        Assert.Equal(11f, collider.WorldVertices[1].Y, 3);
        Assert.Equal(4f, collider.WorldVertices[2].X, 3);
        Assert.Equal(10f, collider.WorldVertices[2].Y, 3);
    }

    [Fact]
    public void ConvexCollider_ThrowsOnLessThan3Vertices()
    {
        Assert.Throws<ArgumentException>(() => new ConvexColliderComponent([new(0, 0), new(1, 0)]));
    }

    [Fact]
    public void ConvexCollider_BroadPhaseAABB_UpdatedAfterTransform()
    {
        var model = new Vector2[]
        {
            new(-1, -1),
            new(1, -1),
            new(1, 1),
            new(-1, 1),
        };
        var collider = new ConvexColliderComponent(model);
        var transform = new TransformComponent(new Vector2(100, 200));

        collider.UpdateWorldVertices(transform);

        Assert.Equal(99f, collider.BroadPhaseAABB.Left, 3);
        Assert.Equal(199f, collider.BroadPhaseAABB.Top, 3);
        Assert.Equal(101f, collider.BroadPhaseAABB.Right, 3);
        Assert.Equal(201f, collider.BroadPhaseAABB.Bottom, 3);
    }

    // ---- Collider world geometry derives from the entity's WORLD transform (PF-G item 1) ----
    // Repro-first: a convex collider on a CHILD (a prefab instance's child) must sit at its WORLD
    // position once the root is placed. On HEAD this FAILED — the derivation read local Position, so
    // the child collider ignored the root's placement ("more to the top and right").

    [Fact]
    public void ConvexCollider_ChildEntity_WorldVertices_IncludeParentWorldPosition()
    {
        var model = new Vector2[] { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };
        var collider = new ConvexColliderComponent(model);

        // Instance root placed at a cursor position; the collider-bearing child is offset locally.
        var root = new TransformComponent(new Vector2(100, 40));
        var child = new TransformComponent(new Vector2(5, -3)) { Parent = root };
        Assert.Equal(new Vector2(105, 37), child.WorldPosition); // sanity: composed world position

        collider.UpdateWorldVertices(child);

        // world = model + child.WorldPosition (the root's placement folded in) — NOT model + local.
        Assert.Equal(new Vector2(104, 36), collider.WorldVertices[0]);
        Assert.Equal(new Vector2(106, 36), collider.WorldVertices[1]);
        Assert.Equal(new Vector2(106, 38), collider.WorldVertices[2]);
        Assert.Equal(new Vector2(104, 38), collider.WorldVertices[3]);
        // The OLD local-only derivation would have produced model + (5,-3), missing the (100,40) root.
        Assert.NotEqual(new Vector2(4, -4), collider.WorldVertices[0]);
    }

    [Fact]
    public void BoxCollider_Root_WorldPosition_ByteIdenticalToLocal()
    {
        // For a ROOT entity WorldPosition == Position, so the box-polygon anchor is byte-identical to
        // the former local anchor — the box-on-root path the user confirmed still places correctly.
        var box = new BoxColliderComponent(new Rectangle(-4, -6, 8, 12));
        var transform = new TransformComponent(new Vector2(50, 70)); // root: no parent
        Assert.Equal(transform.Position, transform.WorldPosition);

        var poly = new Vector2[4];
        SATCollision.BoxToPolygon(box, transform, poly);

        Assert.Equal(new Vector2(46, 64), poly[0]); // TL = pos + (Left, Top)
        Assert.Equal(new Vector2(54, 64), poly[1]); // TR
        Assert.Equal(new Vector2(54, 76), poly[2]); // BR
        Assert.Equal(new Vector2(46, 76), poly[3]); // BL
    }

    [Fact]
    public void BoxCollider_ChildEntity_Polygon_IncludesParentWorldPosition()
    {
        // The box path follows a parent too (byte-identical for roots; world-correct for children).
        var box = new BoxColliderComponent(new Rectangle(0, 0, 10, 10));
        var root = new TransformComponent(new Vector2(200, 90));
        var child = new TransformComponent(new Vector2(-20, 15)) { Parent = root };

        var poly = new Vector2[4];
        SATCollision.BoxToPolygon(box, child, poly);

        // anchor = child.WorldPosition = (180, 105); poly[0] = anchor + (Left, Top) = (180, 105).
        Assert.Equal(new Vector2(180, 105), poly[0]);
        Assert.Equal(new Vector2(190, 115), poly[2]);
    }
}
