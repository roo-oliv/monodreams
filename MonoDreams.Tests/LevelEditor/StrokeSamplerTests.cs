#nullable enable
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Brush;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// The pure arc-length <see cref="StrokeSampler"/> behind the palette's multi-stamp hold-drag
/// (island-authoring Slice 4): even spacing along a stroke, leftover distance carrying between
/// calls, and the disable/short-segment no-ops.
/// </summary>
public class StrokeSamplerTests
{
    [Fact]
    public void Sample_EvenlySpacesAlongTheSegment_ExcludingTheAnchor()
    {
        var points = StrokeSampler.Sample(new Vector2(0, 0), new Vector2(35, 0), spacing: 10f);
        // Strictly after the anchor, spaced 10 apart, never past the end: 10, 20, 30 (not 0, not 40).
        Assert.Equal(new[] { new Vector2(10, 0), new Vector2(20, 0), new Vector2(30, 0) }, points);
    }

    [Fact]
    public void Sample_HonorsSpacingOnADiagonal()
    {
        var points = StrokeSampler.Sample(new Vector2(0, 0), new Vector2(6, 8), spacing: 5f);
        // The segment is length 10 → one stamp at arc-length 5 (halfway), then 10 == the endpoint.
        Assert.Equal(2, points.Count);
        Assert.Equal(new Vector2(3, 4), points[0]);
        Assert.Equal(new Vector2(6, 8), points[1]);
    }

    [Fact]
    public void Sample_LeftoverDistanceCarriesToTheNextCall()
    {
        // Frame 1: from 0 to 12 with spacing 10 → one stamp at 10, anchor advances to 10.
        var first = StrokeSampler.Sample(new Vector2(0, 0), new Vector2(12, 0), 10f);
        Assert.Single(first);
        Assert.Equal(new Vector2(10, 0), first[0]);

        // Frame 2: from the new anchor (10) to 22 → the accumulated distance earns exactly one more.
        var second = StrokeSampler.Sample(new Vector2(10, 0), new Vector2(22, 0), 10f);
        Assert.Single(second);
        Assert.Equal(new Vector2(20, 0), second[0]);
    }

    [Fact]
    public void Sample_ShortSegmentOrNonPositiveSpacing_IsEmpty()
    {
        Assert.Empty(StrokeSampler.Sample(new Vector2(0, 0), new Vector2(9, 0), spacing: 10f));
        Assert.Empty(StrokeSampler.Sample(new Vector2(0, 0), new Vector2(50, 0), spacing: 0f));
        Assert.Empty(StrokeSampler.Sample(new Vector2(0, 0), new Vector2(50, 0), spacing: -5f));
    }
}
