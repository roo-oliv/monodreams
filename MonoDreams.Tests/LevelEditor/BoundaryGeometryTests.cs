using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Proxy;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure boundary bake math (island-authoring Slice 3, plan §5.2): a polyline of N
/// points bakes into N−1 thin convex quad segments, each of the boundary's thickness, wound to the
/// collision module's convention (so SAT resolves them). Also covers the world-projection,
/// centroid, and open-polyline border hit-test the tool / selection / overlay share. Names the
/// premise "A boundary bakes into one convex quad segment per polyline edge; bake products never
/// serialize" in MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class BoundaryGeometryTests
{
    [Fact]
    public void EdgeQuads_ProducesOneConvexQuadPerEdge_OfTheGivenThickness()
    {
        // A 3-point open polyline → 2 edges → 2 quads.
        var points = new List<Vector2> { new(0, 0), new(100, 0), new(100, 100) };
        var quads = BoundaryGeometry.EdgeQuads(points, thickness: 20f);

        Assert.Equal(2, quads.Count);
        foreach (var quad in quads)
        {
            Assert.Equal(4, quad.Length);
            // Each quad is a valid convex polygon (the collider requires it).
            Assert.True(ProxyGeometry.IsConvex(quad));
        }

        // The first quad wraps the horizontal edge (0,0)→(100,0) at ±10 in y: its y-extent spans
        // the thickness, its x-extent spans the edge length.
        var q0 = quads[0];
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in q0)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
        }
        Assert.Equal(0f, minX, 3);
        Assert.Equal(100f, maxX, 3);
        Assert.Equal(-10f, minY, 3);
        Assert.Equal(10f, maxY, 3);
    }

    [Fact]
    public void EdgeQuads_ClockwiseWinding_MatchesTheColliderConvention()
    {
        var points = new List<Vector2> { new(0, 0), new(100, 0) };
        var quads = BoundaryGeometry.EdgeQuads(points, thickness: 8f);
        Assert.Single(quads);
        // Positive shoelace sum is the module's clockwise-in-y-down convention (matches
        // ColliderDefaults.Hexagon, which the collision module accepts).
        Assert.True(BoundaryGeometry.ShoelaceSum(quads[0]) > 0f);
    }

    [Fact]
    public void EdgeQuads_DegenerateInputs_ContributeNothing()
    {
        Assert.Empty(BoundaryGeometry.EdgeQuads(new List<Vector2> { new(0, 0) }, 10f)); // one point, no edge
        Assert.Empty(BoundaryGeometry.EdgeQuads(new List<Vector2> { new(0, 0), new(10, 0) }, 0f)); // no thickness
        // A zero-length edge in the middle is skipped; the surrounding edges still bake.
        var quads = BoundaryGeometry.EdgeQuads(
            new List<Vector2> { new(0, 0), new(0, 0), new(50, 0) }, 10f);
        Assert.Single(quads); // only the (0,0)→(50,0) edge
    }

    [Fact]
    public void WorldPolyline_OffsetsLocalPointsByThePosition()
    {
        var local = new List<Vector2> { new(-10, -10), new(10, 10) };
        var world = BoundaryGeometry.WorldPolyline(local, new Vector2(100, 200));
        Assert.Equal(new Vector2(90, 190), world[0]);
        Assert.Equal(new Vector2(110, 210), world[1]);
    }

    [Fact]
    public void Centroid_IsTheArithmeticMean()
    {
        var c = BoundaryGeometry.Centroid(new List<Vector2> { new(0, 0), new(100, 0), new(100, 100), new(0, 100) });
        Assert.Equal(new Vector2(50, 50), c);
    }

    [Fact]
    public void PolylineContains_HitsOpenEdgesOnly_NotTheClosingEdge()
    {
        // An L-shaped open polyline: (0,0)→(100,0)→(100,100).
        var poly = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100) };
        Assert.True(BoundaryGeometry.PolylineContains(poly, new Vector2(50, 1), tolerance: 4f)); // on edge 1
        Assert.True(BoundaryGeometry.PolylineContains(poly, new Vector2(99, 50), tolerance: 4f)); // on edge 2
        // The closing edge (100,100)→(0,0) is NOT part of an open boundary — its midpoint (50,50)
        // is far from both real edges, so it must miss.
        Assert.False(BoundaryGeometry.PolylineContains(poly, new Vector2(50, 50), tolerance: 4f));
    }
}
