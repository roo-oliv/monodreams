#nullable enable
using System;
using System.Collections.Generic;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>
/// The pure, GraphicsDevice-free planning behind the editor's world-space reference grid (UX3-D §3) —
/// separated from <c>EditorGrid</c> (which owns the mesh entity + the world/projection emission) so the
/// line placement and the <b>bounded vertex count</b> (pre-mortem #5) are directly unit-testable.
/// Mirrors the <c>CameraNav</c> / <c>GizmoTransform</c> / <c>CameraEntityGlyph</c> split.
///
/// <para><b>Anchoring + cadence.</b> A grid line sits at every integer multiple <c>k · spacing</c> of
/// the spacing that falls inside the visible world AABB. A line is <b>major</b> (drawn stronger) iff its
/// index <c>k</c> is a multiple of <see cref="MajorEvery"/> — so majors are anchored to the world origin
/// (multiples of <c>5 · spacing</c>), exactly Blender's every-5th-cell rule.</para>
///
/// <para><b>Bounded degradation (pre-mortem #5).</b> A zoomed-out view over a small spacing must never
/// allocate an unbounded mesh. When the minor line count per axis exceeds
/// <see cref="MinorLineCapPerAxis"/> the grid degrades to <see cref="GridDensity.MajorOnly"/> (only the
/// every-5th lines); when even the major count per axis exceeds <see cref="MajorLineCapPerAxis"/> it
/// degrades to <see cref="GridDensity.None"/> (the grid is meaningless that far out). Either way the
/// emitted line count is bounded by <c>2 · <see cref="MinorLineCapPerAxis"/></c>.</para>
/// </summary>
public static class GridGeometry
{
    /// <summary>Every Nth line is a major (stronger) line, anchored to the world origin.</summary>
    public const int MajorEvery = 5;

    /// <summary>Above this many MINOR lines on either axis the grid drops to major-only.</summary>
    public const int MinorLineCapPerAxis = 200;

    /// <summary>Above this many MAJOR lines on either axis the grid draws nothing.</summary>
    public const int MajorLineCapPerAxis = 200;

    /// <summary>How dense a grid a <see cref="GridPlan"/> resolved to (the bounded-degradation ladder).</summary>
    public enum GridDensity
    {
        /// <summary>Too far out to mean anything — no lines.</summary>
        None,
        /// <summary>Zoomed out — only the every-5th (major) lines.</summary>
        MajorOnly,
        /// <summary>The full grid — every minor line, with every 5th drawn as a major.</summary>
        Full,
    }

    /// <summary>One grid line's placement: the world coordinate it sits at (X for a vertical line, Y for
    /// a horizontal one) and whether it is a major (stronger) line.</summary>
    public readonly record struct GridAxisLine(float Coordinate, bool Major);

    /// <summary>The resolved grid for a visible world AABB + spacing: the vertical lines (constant world
    /// X, spanning the AABB's Y range) and horizontal lines (constant world Y, spanning the X range),
    /// plus the density the caller can log/assert. <see cref="LineCount"/> is bounded (pre-mortem #5).</summary>
    public readonly struct GridPlan
    {
        public GridDensity Density { get; }
        public IReadOnlyList<GridAxisLine> VerticalLines { get; }
        public IReadOnlyList<GridAxisLine> HorizontalLines { get; }

        public GridPlan(GridDensity density, IReadOnlyList<GridAxisLine> verticalLines,
            IReadOnlyList<GridAxisLine> horizontalLines)
        {
            Density = density;
            VerticalLines = verticalLines;
            HorizontalLines = horizontalLines;
        }

        /// <summary>The total number of grid lines (vertical + horizontal). Bounded by
        /// <c>2 · <see cref="MinorLineCapPerAxis"/></c> regardless of zoom.</summary>
        public int LineCount => VerticalLines.Count + HorizontalLines.Count;

        public static GridPlan Empty { get; } =
            new(GridDensity.None, Array.Empty<GridAxisLine>(), Array.Empty<GridAxisLine>());
    }

    /// <summary>
    /// Plans the grid lines for the visible world AABB <c>[left,right] × [top,bottom]</c> at
    /// <paramref name="spacing"/> world units. A non-positive spacing or a degenerate AABB yields
    /// <see cref="GridPlan.Empty"/>. The density degrades per the caps so the line count stays bounded.
    /// </summary>
    public static GridPlan Plan(float left, float top, float right, float bottom, float spacing)
    {
        if (spacing <= 0f || right <= left || bottom <= top) return GridPlan.Empty;

        // Inclusive index range of lines inside the AABB on each axis.
        var firstKx = (int)Math.Ceiling(left / spacing);
        var lastKx = (int)Math.Floor(right / spacing);
        var firstKy = (int)Math.Ceiling(top / spacing);
        var lastKy = (int)Math.Floor(bottom / spacing);

        var countX = Math.Max(0, lastKx - firstKx + 1);
        var countY = Math.Max(0, lastKy - firstKy + 1);
        if (countX == 0 && countY == 0) return GridPlan.Empty;

        var minorPerAxis = Math.Max(countX, countY);
        GridDensity density;
        if (minorPerAxis <= MinorLineCapPerAxis)
        {
            density = GridDensity.Full;
        }
        else
        {
            var majorPerAxis = Math.Max(
                MultiplesOf(MajorEvery, firstKx, lastKx),
                MultiplesOf(MajorEvery, firstKy, lastKy));
            density = majorPerAxis <= MajorLineCapPerAxis ? GridDensity.MajorOnly : GridDensity.None;
        }

        if (density == GridDensity.None) return GridPlan.Empty;

        var majorOnly = density == GridDensity.MajorOnly;
        return new GridPlan(
            density,
            BuildAxis(firstKx, lastKx, spacing, majorOnly),
            BuildAxis(firstKy, lastKy, spacing, majorOnly));
    }

    /// <summary>The lines for one axis: every index (full) or only the every-5th indices (major-only).</summary>
    private static GridAxisLine[] BuildAxis(int firstK, int lastK, float spacing, bool majorOnly)
    {
        if (lastK < firstK) return Array.Empty<GridAxisLine>();
        var lines = new List<GridAxisLine>(lastK - firstK + 1);
        for (var k = firstK; k <= lastK; k++)
        {
            var major = k % MajorEvery == 0;
            if (majorOnly && !major) continue;
            lines.Add(new GridAxisLine(k * spacing, major));
        }
        return lines.ToArray();
    }

    /// <summary>Count of multiples of <paramref name="n"/> in the inclusive index range [a, b].</summary>
    private static int MultiplesOf(int n, int a, int b)
    {
        if (b < a) return 0;
        return (int)(Math.Floor(b / (double)n) - Math.Ceiling(a / (double)n)) + 1;
    }
}
