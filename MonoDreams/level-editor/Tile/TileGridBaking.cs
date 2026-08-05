#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Level;

namespace MonoDreams.LevelEditor.Tile;

/// <summary>
/// The pure math behind <c>TileGridBakeSystem</c> — world-free and GraphicsDevice-free so the
/// autotile pick and the collider merge are directly unit-testable:
/// <list type="bullet">
///   <item><see cref="NeighborMask"/> — the 4-bit same-neighbor mask (U=1, R=2, D=4, L=8; a bit is
///   SET when that neighbor holds the SAME value id). "The tile above is not wall" is simply
///   "bit U unset".</item>
///   <item><see cref="ParseRules"/> — the <c>mask:col,row|col,row</c> DSL on
///   <see cref="TilePaintValue.AutotileRules"/> into a 16-entry alternates table.</item>
///   <item><see cref="PickTile"/> — the source rect for a cell: its mask's entry (fallback: the
///   15/interior entry, then sheet cell 0,0), alternates picked by a deterministic cell hash so a
///   repaint never reshuffles the variation.</item>
///   <item><see cref="MergeRectangles"/> — greedy row-run + column merge of a value's cells into
///   the FEWEST axis-aligned rectangles. Colliders bake merged, never per-cell: flush-adjacent
///   colliders seam-catch swept AABBs (the physics premise), and one rectangle per stretch is what
///   the reference levels hand-author.</item>
/// </list>
/// </summary>
public static class TileGridBaking
{
    public const int MaskUp = 1;
    public const int MaskRight = 2;
    public const int MaskDown = 4;
    public const int MaskLeft = 8;

    /// <summary>The 4-bit same-neighbor mask of cell (<paramref name="x"/>, <paramref name="y"/>)
    /// for <paramref name="value"/> in <paramref name="cells"/>.</summary>
    public static int NeighborMask(Dictionary<long, byte> cells, int x, int y, byte value)
    {
        var mask = 0;
        if (Same(cells, x, y - 1, value)) mask |= MaskUp;
        if (Same(cells, x + 1, y, value)) mask |= MaskRight;
        if (Same(cells, x, y + 1, value)) mask |= MaskDown;
        if (Same(cells, x - 1, y, value)) mask |= MaskLeft;
        return mask;
    }

    private static bool Same(Dictionary<long, byte> cells, int x, int y, byte value) =>
        cells.TryGetValue(TileGridComponent.Pack(x, y), out var v) && v == value;

    /// <summary>
    /// Parses the autotile DSL into a 16-entry table of tileset-cell alternates. Entries look like
    /// <c>"6:0,0"</c> or <c>"15:1,1|6,0|7,0"</c>, whitespace-separated; unknown/garbled entries are
    /// skipped (loud is the caller's choice). A null/empty DSL yields a table whose every mask maps
    /// to cell (0,0).
    /// </summary>
    public static Point[][] ParseRules(string? rules)
    {
        var table = new Point[16][];
        if (!string.IsNullOrWhiteSpace(rules))
        {
            foreach (var entry in rules.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = entry.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(entry.AsSpan(0, colon), out var mask) || mask is < 0 or > 15) continue;

                var alternates = new List<Point>();
                foreach (var alt in entry.Substring(colon + 1).Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var comma = alt.IndexOf(',');
                    if (comma <= 0) continue;
                    if (int.TryParse(alt.AsSpan(0, comma), out var col) &&
                        int.TryParse(alt.AsSpan(comma + 1), out var row))
                        alternates.Add(new Point(col, row));
                }
                if (alternates.Count > 0) table[mask] = alternates.ToArray();
            }
        }

        // Fallback chain: an unmapped mask uses the interior (15) entry, else sheet cell (0,0).
        var interior = table[15] ?? new[] { Point.Zero };
        for (var i = 0; i < 16; i++) table[i] ??= interior;
        return table;
    }

    /// <summary>The tileset source rectangle for a cell, given its parsed rules and mask. The
    /// alternate is picked by a deterministic (x, y) hash — stable across repaints and loads.</summary>
    public static Rectangle PickTile(Point[][] rules, int mask, int x, int y, int tileSize)
    {
        var alternates = rules[mask & 15];
        var pick = alternates.Length == 1 ? alternates[0] : alternates[CellHash(x, y) % alternates.Length];
        return new Rectangle(pick.X * tileSize, pick.Y * tileSize, tileSize, tileSize);
    }

    private static int CellHash(int x, int y)
    {
        unchecked
        {
            var h = x * 73856093 ^ y * 19349663;
            return h < 0 ? -h : h;
        }
    }

    /// <summary>
    /// Greedy-merges every cell holding <paramref name="value"/> into axis-aligned cell rectangles:
    /// maximal horizontal runs per row, then runs of identical (x, width) merged downward. Output
    /// rectangles are in CELL units (x, y, width, height), deterministic (sorted by y then x).
    /// </summary>
    public static List<Rectangle> MergeRectangles(Dictionary<long, byte> cells, byte value)
    {
        // Collect the value's cells row by row.
        var rows = new SortedDictionary<int, List<int>>();
        foreach (var (key, v) in cells)
        {
            if (v != value) continue;
            var (x, y) = TileGridComponent.Unpack(key);
            if (!rows.TryGetValue(y, out var xs)) rows[y] = xs = new List<int>();
            xs.Add(x);
        }

        // Horizontal runs per row.
        var runs = new List<(int X, int Y, int Width)>();
        foreach (var (y, xs) in rows)
        {
            xs.Sort();
            var start = xs[0];
            var previous = xs[0];
            for (var i = 1; i <= xs.Count; i++)
            {
                if (i < xs.Count && xs[i] == previous + 1)
                {
                    previous = xs[i];
                    continue;
                }
                runs.Add((start, y, previous - start + 1));
                if (i < xs.Count)
                {
                    start = xs[i];
                    previous = xs[i];
                }
            }
        }

        // Group runs by row, then sweep rows ascending: a run whose (x, width) matches an ACTIVE
        // rectangle ending on the previous row extends it downward; anything not extended emits.
        var runsByRow = new SortedDictionary<int, List<(int X, int Width)>>();
        foreach (var run in runs)
        {
            if (!runsByRow.TryGetValue(run.Y, out var list)) runsByRow[run.Y] = list = new List<(int, int)>();
            list.Add((run.X, run.Width));
        }

        var rects = new List<Rectangle>();
        var active = new Dictionary<(int X, int Width), Rectangle>();
        foreach (var (y, rowRuns) in runsByRow)
        {
            var next = new Dictionary<(int X, int Width), Rectangle>();
            foreach (var (x, width) in rowRuns)
            {
                if (active.TryGetValue((x, width), out var above) && above.Y + above.Height == y)
                    next[(x, width)] = new Rectangle(above.X, above.Y, above.Width, above.Height + 1);
                else
                    next[(x, width)] = new Rectangle(x, y, width, 1);
            }
            foreach (var (key, rect) in active)
                if (!next.TryGetValue(key, out var extended) || extended.Y != rect.Y)
                    rects.Add(rect);
            active = next;
        }
        rects.AddRange(active.Values);

        rects.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        return rects;
    }
}
