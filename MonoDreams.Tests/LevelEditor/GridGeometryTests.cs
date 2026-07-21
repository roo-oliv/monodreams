using System.Linq;
using MonoDreams.LevelEditor.Transform;
using Xunit;
using static MonoDreams.LevelEditor.Transform.GridGeometry;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure grid-line planning (UX3-D §3): lines at the spacing across the visible world
/// range, the every-5th major cadence anchored to the world origin, and — the load-bearing pre-mortem
/// #5 property — the BOUNDED line count as the view zooms out (Full → MajorOnly → None), so a
/// zoomed-out view over a small spacing can never allocate an unbounded mesh.
/// </summary>
public class GridGeometryTests
{
    [Fact]
    public void Plan_EmitsLinesAtSpacing_OverTheVisibleRange()
    {
        // Visible world [-40,40] × [-30,30] at spacing 16.
        var plan = Plan(-40f, -30f, 40f, 30f, 16f);

        Assert.Equal(GridDensity.Full, plan.Density);
        // Vertical lines: multiples of 16 in [-40,40] → -32,-16,0,16,32.
        Assert.Equal(new[] { -32f, -16f, 0f, 16f, 32f },
            plan.VerticalLines.Select(l => l.Coordinate).ToArray());
        // Horizontal lines: multiples of 16 in [-30,30] → -16,0,16.
        Assert.Equal(new[] { -16f, 0f, 16f },
            plan.HorizontalLines.Select(l => l.Coordinate).ToArray());
    }

    [Fact]
    public void Plan_MajorLines_AreEveryFifth_AnchoredToTheOrigin()
    {
        // [-80,80] at spacing 16 → indices k=-5..5; majors are k%5==0 → x=-80,0,80.
        var plan = Plan(-80f, -80f, 80f, 80f, 16f);

        var majors = plan.VerticalLines.Where(l => l.Major).Select(l => l.Coordinate).ToArray();
        Assert.Equal(new[] { -80f, 0f, 80f }, majors);
        // 0 is major (origin-anchored); 16/32/48/64 are minor.
        Assert.True(plan.VerticalLines.Single(l => l.Coordinate == 0f).Major);
        Assert.False(plan.VerticalLines.Single(l => l.Coordinate == 16f).Major);
        Assert.False(plan.VerticalLines.Single(l => l.Coordinate == 64f).Major);
    }

    [Fact]
    public void Plan_DegradesToMajorOnly_ThenToNothing_AsTheRangeGrows()
    {
        // Moderate: 101 minor lines/axis ≤ cap → full grid.
        Assert.Equal(GridDensity.Full, Plan(0f, 0f, 1600f, 1600f, 16f).Density);

        // Zoomed out: 501 minor/axis > cap, but ~101 major/axis ≤ cap → major-only.
        var majorOnly = Plan(0f, 0f, 8000f, 8000f, 16f);
        Assert.Equal(GridDensity.MajorOnly, majorOnly.Density);
        Assert.All(majorOnly.VerticalLines, l => Assert.True(l.Major)); // only majors survive
        Assert.All(majorOnly.HorizontalLines, l => Assert.True(l.Major));

        // Extreme: majors themselves blow the cap → nothing (the grid is meaningless that far out).
        var none = Plan(-100000f, -100000f, 100000f, 100000f, 1f);
        Assert.Equal(GridDensity.None, none.Density);
        Assert.Equal(0, none.LineCount);
    }

    [Theory]
    [InlineData(0f, 0f, 1600f, 1600f, 16f)]     // full
    [InlineData(0f, 0f, 8000f, 8000f, 16f)]     // major-only
    [InlineData(-1e6f, -1e6f, 1e6f, 1e6f, 0.5f)] // none
    [InlineData(-5e5f, 0f, 5e5f, 2000f, 4f)]    // asymmetric extreme
    public void Plan_LineCount_IsAlwaysBounded_RegardlessOfZoom(
        float l, float t, float r, float b, float spacing)
    {
        var plan = Plan(l, t, r, b, spacing);
        // The hard pre-mortem #5 bound: never more than 2 axes × the per-axis cap, whatever the zoom.
        Assert.True(plan.LineCount <= 2 * MinorLineCapPerAxis,
            $"line count {plan.LineCount} exceeded the bound {2 * MinorLineCapPerAxis}");
    }

    [Fact]
    public void Plan_DegenerateOrNonPositiveSpacing_IsEmpty()
    {
        Assert.Equal(0, Plan(0f, 0f, 100f, 100f, 0f).LineCount);   // spacing 0
        Assert.Equal(0, Plan(0f, 0f, 100f, 100f, -16f).LineCount); // negative spacing
        Assert.Equal(0, Plan(100f, 100f, 0f, 0f, 16f).LineCount);  // inverted AABB
    }
}
