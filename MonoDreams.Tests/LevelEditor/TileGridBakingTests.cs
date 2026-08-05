using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Tile;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure maths behind the paint-grid bake (<c>TileGridBaking</c>) — world-free and
/// <c>GraphicsDevice</c>-free by design, so the autotile pick and the collider merge are directly
/// unit-testable:
/// <list type="bullet">
///   <item><c>NeighborMask</c> — the 4-bit SAME-neighbor mask (U=1, R=2, D=4, L=8; a bit is SET only
///   when that orthogonal neighbor holds the SAME value id — diagonals never participate).</item>
///   <item><c>ParseRules</c> — the <c>mask:col,row|col,row</c> DSL into a 16-entry alternates table,
///   with the 15/interior entry as the fallback for unmapped masks and garbled entries skipped
///   rather than thrown.</item>
///   <item><c>PickTile</c> — the tileset source rect for a cell, with alternates picked by a
///   DETERMINISTIC cell hash (a repaint must never reshuffle the terrain's variation).</item>
///   <item><c>MergeRectangles</c> — greedy row-run + column merge into the fewest axis-aligned
///   rectangles, and the load-bearing NO-SEAMS property: no two output rects are flush-adjacent
///   across a full shared edge, because flush-adjacent colliders seam-catch swept AABBs.</item>
/// </list>
///
/// Covers the level-loading premise "The paint grid is authored cells + values; everything
/// visible/collidable is a bake product" (its derivation half) and the level-editor premise
/// "Tile sprites stream per chunk; colliders bake whole" (its merge half).
/// </summary>
public class TileGridBakingTests
{
    private const byte Wall = 1;
    private const byte Other = 2;

    // ---- helpers ----------------------------------------------------------------------------

    private static Dictionary<long, byte> Paint(byte value, params (int X, int Y)[] cells)
    {
        var map = new Dictionary<long, byte>();
        PaintInto(map, value, cells);
        return map;
    }

    private static void PaintInto(Dictionary<long, byte> map, byte value, params (int X, int Y)[] cells)
    {
        foreach (var (x, y) in cells) map[TileGridComponent.Pack(x, y)] = value;
    }

    private static (int X, int Y)[] Block(int x0, int y0, int width, int height)
    {
        var cells = new List<(int X, int Y)>();
        for (var y = y0; y < y0 + height; y++)
        for (var x = x0; x < x0 + width; x++)
            cells.Add((x, y));
        return cells.ToArray();
    }

    /// <summary>The shapes the no-seams / coverage properties are asserted over. The blob uses our
    /// OWN fixed hash (not <c>System.Random</c>) so the shape is byte-stable across runtimes.</summary>
    private static (int X, int Y)[] Shape(string name)
    {
        switch (name)
        {
            case "rectangle":
                return Block(0, 0, 5, 4);
            case "L":
                return new (int X, int Y)[] { (0, 0), (0, 1), (0, 2), (1, 2), (2, 2) };
            case "plus":
                return new (int X, int Y)[] { (1, 0), (0, 1), (1, 1), (2, 1), (1, 2) };
            default:
                return Blob();
        }
    }

    private static (int X, int Y)[] Blob()
    {
        var cells = new List<(int X, int Y)>();
        for (var y = 0; y < 9; y++)
        for (var x = 0; x < 12; x++)
        {
            var h = unchecked(((uint)x * 374761393u) ^ ((uint)y * 668265263u) ^ 0x9E3779B9u);
            h ^= h >> 13;
            h = unchecked(h * 1274126177u);
            h ^= h >> 16;
            if ((h & 3u) != 0u) cells.Add((x, y)); // ~3 cells in 4 → an irregular patch with holes
        }
        return cells.ToArray();
    }

    public static IEnumerable<object[]> PaintedShapes() => new[]
    {
        new object[] { "rectangle" },
        new object[] { "L" },
        new object[] { "plus" },
        new object[] { "blob" },
    };

    // ---- NeighborMask ----------------------------------------------------------------------

    [Fact]
    public void NeighborMask_AllFourSidesSameValue_IsFifteen()
    {
        var cells = Paint(Wall, (5, 5), (5, 4), (6, 5), (5, 6), (4, 5));

        Assert.Equal(15, TileGridBaking.NeighborMask(cells, 5, 5, Wall));
        Assert.Equal(TileGridBaking.MaskUp | TileGridBaking.MaskRight
                     | TileGridBaking.MaskDown | TileGridBaking.MaskLeft,
            TileGridBaking.NeighborMask(cells, 5, 5, Wall));
    }

    [Fact]
    public void NeighborMask_IsolatedCell_IsZero()
    {
        var cells = Paint(Wall, (5, 5));

        Assert.Equal(0, TileGridBaking.NeighborMask(cells, 5, 5, Wall));
    }

    [Theory]
    [InlineData(0, -1, TileGridBaking.MaskUp)]
    [InlineData(1, 0, TileGridBaking.MaskRight)]
    [InlineData(0, 1, TileGridBaking.MaskDown)]
    [InlineData(-1, 0, TileGridBaking.MaskLeft)]
    public void NeighborMask_SingleNeighbor_SetsExactlyItsBit(int dx, int dy, int expected)
    {
        // The bit layout is a CONTRACT the autotile rule DSL is authored against (U=1 R=2 D=4 L=8);
        // swapping two bits silently re-skins every hand-written rule set.
        var cells = Paint(Wall, (0, 0), (dx, dy));

        Assert.Equal(expected, TileGridBaking.NeighborMask(cells, 0, 0, Wall));
    }

    [Fact]
    public void NeighborMask_NeighborWithADifferentValue_DoesNotSetTheBit()
    {
        // "The tile above is not wall" must read as "bit U unset" — a DIFFERENT paint value is not
        // the same value, even though the cell is occupied.
        var cells = Paint(Wall, (0, 0));
        PaintInto(cells, Other, (0, -1), (1, 0), (0, 1), (-1, 0));

        Assert.Equal(0, TileGridBaking.NeighborMask(cells, 0, 0, Wall));
        // ...and the mask is per-value: asked about the OTHER value, the very same four neighbors
        // all count (the centre cell's own value is irrelevant to the mask).
        Assert.Equal(15, TileGridBaking.NeighborMask(cells, 0, 0, Other));
    }

    [Fact]
    public void NeighborMask_DiagonalNeighbors_NeverCount()
    {
        // Only the 4 orthogonal sides participate — a 4-bit mask has no room for corners, and the
        // rule DSL is authored on that assumption.
        var cells = Paint(Wall, (0, 0), (-1, -1), (1, -1), (-1, 1), (1, 1));

        Assert.Equal(0, TileGridBaking.NeighborMask(cells, 0, 0, Wall));
    }

    // ---- ParseRules ------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseRules_NullOrEmptyDsl_MapsEveryMaskToSheetCellZero(string? dsl)
    {
        var table = TileGridBaking.ParseRules(dsl);

        Assert.Equal(16, table.Length);
        for (var mask = 0; mask < 16; mask++)
        {
            Assert.NotNull(table[mask]);
            Assert.Single(table[mask]);
            Assert.Equal(Point.Zero, table[mask][0]);
        }
    }

    [Fact]
    public void ParseRules_InteriorEntry_IsTheFallbackForUnmappedMasks()
    {
        // A sheet that only bothers to name the interior tile still renders every mask (with the
        // interior tile) instead of falling back to sheet cell 0,0 for 15 of 16 cases.
        var table = TileGridBaking.ParseRules("15:4,5");

        for (var mask = 0; mask < 16; mask++)
        {
            Assert.Single(table[mask]);
            Assert.Equal(new Point(4, 5), table[mask][0]);
        }
    }

    [Fact]
    public void ParseRules_ExplicitPerMaskEntries_ParseToTheirPoint()
    {
        var table = TileGridBaking.ParseRules("0:2,3 6:1,0 15:1,1");

        Assert.Equal(new Point(2, 3), Assert.Single(table[0]));
        Assert.Equal(new Point(1, 0), Assert.Single(table[6]));
        Assert.Equal(new Point(1, 1), Assert.Single(table[15]));
        // Unmapped masks fall back to the 15/interior entry, not to 0,0.
        Assert.Equal(new Point(1, 1), Assert.Single(table[3]));
        Assert.Equal(new Point(1, 1), Assert.Single(table[9]));
    }

    [Fact]
    public void ParseRules_AlternatesSplitOnPipe()
    {
        var table = TileGridBaking.ParseRules("15:1,1|6,0|7,0 6:0,0");

        Assert.Equal(3, table[15].Length);
        Assert.Equal(new Point(1, 1), table[15][0]);
        Assert.Equal(new Point(6, 0), table[15][1]);
        Assert.Equal(new Point(7, 0), table[15][2]);
        // A single-alternate entry stays single (no accidental splitting).
        Assert.Equal(new Point(0, 0), Assert.Single(table[6]));
    }

    [Fact]
    public void ParseRules_GarbledEntries_AreSkippedWithoutThrowing()
    {
        // A hand-typed rule string is designer input: a typo must cost that one entry, never the
        // whole level's terrain (and never an exception mid-bake).
        var table = TileGridBaking.ParseRules("nocolon 16:0,0 -1:1,1 abc:1,2 5:x,y 7: :0,0 9:1 12:2,4");

        Assert.Equal(new Point(2, 4), Assert.Single(table[12])); // the one well-formed entry survives
        for (var mask = 0; mask < 16; mask++)
        {
            if (mask == 12) continue;
            // No 15 entry parsed, so the fallback chain lands on sheet cell 0,0.
            Assert.Equal(Point.Zero, Assert.Single(table[mask]));
        }
    }

    // ---- PickTile --------------------------------------------------------------------------

    [Fact]
    public void PickTile_ReturnsTheSheetCellRect()
    {
        var table = TileGridBaking.ParseRules("6:3,2");

        var source = TileGridBaking.PickTile(table, mask: 6, x: 11, y: -4, tileSize: 16);

        Assert.Equal(new Rectangle(3 * 16, 2 * 16, 16, 16), source);
    }

    [Fact]
    public void PickTile_WithAlternates_IsDeterministicAcrossCallsAndRebuiltTables()
    {
        // A repaint (or a reload) re-parses the DSL and re-picks every cell. If the pick were random
        // the terrain's variation would reshuffle on every bake — visible churn, and a scene whose
        // look is not reproducible from its file.
        const string dsl = "15:0,0|1,0|2,0";
        var first = TileGridBaking.ParseRules(dsl);
        var rebuilt = TileGridBaking.ParseRules(dsl);

        for (var y = -3; y <= 4; y++)
        for (var x = -3; x <= 4; x++)
        {
            var a = TileGridBaking.PickTile(first, 15, x, y, 8);
            var b = TileGridBaking.PickTile(first, 15, x, y, 8); // same table, repeated call
            var c = TileGridBaking.PickTile(rebuilt, 15, x, y, 8); // freshly parsed table

            Assert.Equal(a, b);
            Assert.Equal(a, c);
        }
    }

    [Fact]
    public void PickTile_WithAlternates_SpreadsAcrossTheCells()
    {
        // Deterministic must not mean constant: the alternates exist to break up a flat expanse.
        var table = TileGridBaking.ParseRules("15:0,0|1,0|2,0");

        var picked = new HashSet<Rectangle>();
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            picked.Add(TileGridBaking.PickTile(table, 15, x, y, 8));

        Assert.True(picked.Count >= 2,
            $"expected the cell hash to reach at least two alternates, saw {picked.Count}");
    }

    // ---- MergeRectangles -------------------------------------------------------------------

    [Fact]
    public void MergeRectangles_SingleCell_IsOneUnitRect()
    {
        var rects = TileGridBaking.MergeRectangles(Paint(Wall, (3, 7)), Wall);

        Assert.Equal(new Rectangle(3, 7, 1, 1), Assert.Single(rects));
    }

    [Fact]
    public void MergeRectangles_HorizontalRun_IsOneWideRect()
    {
        var rects = TileGridBaking.MergeRectangles(Paint(Wall, (0, 0), (1, 0), (2, 0), (3, 0)), Wall);

        Assert.Equal(new Rectangle(0, 0, 4, 1), Assert.Single(rects));
    }

    [Fact]
    public void MergeRectangles_VerticalStackOfIdenticalRuns_IsOneBlock()
    {
        // 3 wide x 2 tall: the row runs match on (x, width), so they merge downward into one rect.
        var rects = TileGridBaking.MergeRectangles(Paint(Wall, Block(0, 0, 3, 2)), Wall);

        Assert.Equal(new Rectangle(0, 0, 3, 2), Assert.Single(rects));
    }

    [Fact]
    public void MergeRectangles_LShape_DecomposesIntoTheMinimalTwoRects()
    {
        // A 3-tall column with a 2-cell foot: the stem merges vertically (1x2), the foot row emits
        // as one 3x1 run — two rects for five cells, which is the minimum for an L.
        var rects = TileGridBaking.MergeRectangles(
            Paint(Wall, (0, 0), (0, 1), (0, 2), (1, 2), (2, 2)), Wall);

        Assert.Equal(2, rects.Count);
        Assert.Equal(new Rectangle(0, 0, 1, 2), rects[0]);
        Assert.Equal(new Rectangle(0, 2, 3, 1), rects[1]);
    }

    [Fact]
    public void MergeRectangles_OnlyMergesTheRequestedValue()
    {
        // Two paints touching side by side must never merge into one collider: they carry different
        // layers/identity, and the bake asks per value.
        var cells = Paint(Wall, (0, 0), (1, 0));
        PaintInto(cells, Other, (2, 0), (3, 0));

        Assert.Equal(new Rectangle(0, 0, 2, 1),
            Assert.Single(TileGridBaking.MergeRectangles(cells, Wall)));
        Assert.Equal(new Rectangle(2, 0, 2, 1),
            Assert.Single(TileGridBaking.MergeRectangles(cells, Other)));
        // A value nobody painted merges to nothing (no phantom collider).
        Assert.Empty(TileGridBaking.MergeRectangles(cells, 9));
    }

    [Theory]
    [MemberData(nameof(PaintedShapes))]
    public void MergeRectangles_OutputIsSortedByYThenX(string shape)
    {
        // Deterministic ordering is what makes the baked collider NAMES (`Wall_00`, `Wall_01`, …)
        // stable across bakes — and a dictionary-iteration-ordered output would not be.
        var rects = TileGridBaking.MergeRectangles(Paint(Wall, Shape(shape)), Wall);

        for (var i = 1; i < rects.Count; i++)
        {
            var previous = rects[i - 1];
            var current = rects[i];
            Assert.True(previous.Y < current.Y || (previous.Y == current.Y && previous.X < current.X),
                $"{previous} must sort before {current} under (y, x)");
        }
    }

    [Fact]
    public void MergeRectangles_NegativeCoordinates_Work()
    {
        // Cells are signed (the grid entity's transform is the anchor, so painting up/left of it is
        // ordinary) — the packing has to carry the sign through the merge.
        var single = TileGridBaking.MergeRectangles(Paint(Wall, (-3, -2), (-2, -2), (-1, -2)), Wall);
        Assert.Equal(new Rectangle(-3, -2, 3, 1), Assert.Single(single));

        var block = TileGridBaking.MergeRectangles(Paint(Wall, Block(-1, -1, 2, 2)), Wall);
        Assert.Equal(new Rectangle(-1, -1, 2, 2), Assert.Single(block));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(-1, -1)]
    [InlineData(-7, 13)]
    [InlineData(int.MinValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void PackUnpack_RoundTripsSignedCellCoordinates(int x, int y)
    {
        var (roundTripX, roundTripY) = TileGridComponent.Unpack(TileGridComponent.Pack(x, y));

        Assert.Equal(x, roundTripX);
        Assert.Equal(y, roundTripY);
    }

    [Theory]
    [MemberData(nameof(PaintedShapes))]
    public void MergeRectangles_ProducesNoFlushAdjacentSeams(string shape)
    {
        // THE property the merge exists for: two flush-adjacent colliders whose union would be an
        // exact rectangle form a seam that catches a swept AABB sliding along them. If such a pair
        // survives the merge, the merge did not do its job.
        var rects = TileGridBaking.MergeRectangles(Paint(Wall, Shape(shape)), Wall);

        for (var i = 0; i < rects.Count; i++)
        for (var j = 0; j < rects.Count; j++)
        {
            if (i == j) continue;
            var a = rects[i];
            var b = rects[j];

            var verticalSeam = a.X == b.X && a.Width == b.Width && a.Y + a.Height == b.Y;
            Assert.False(verticalSeam, $"{a} and {b} are vertically flush over a full shared edge");

            var horizontalSeam = a.Y == b.Y && a.Height == b.Height && a.X + a.Width == b.X;
            Assert.False(horizontalSeam, $"{a} and {b} are horizontally flush over a full shared edge");
        }
    }

    [Theory]
    [MemberData(nameof(PaintedShapes))]
    public void MergeRectangles_CoversExactlyThePaintedCells(string shape)
    {
        // No gap (a hole in the terrain the player falls through) and no overlap (two colliders on
        // one cell, so a contact resolves twice).
        var painted = Shape(shape);
        var rects = TileGridBaking.MergeRectangles(Paint(Wall, painted), Wall);

        var covered = new HashSet<(int X, int Y)>();
        foreach (var rect in rects)
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        for (var x = rect.X; x < rect.X + rect.Width; x++)
            Assert.True(covered.Add((x, y)), $"cell ({x},{y}) is covered by more than one rect");

        Assert.Equal(
            painted.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray(),
            covered.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray());
    }
}
