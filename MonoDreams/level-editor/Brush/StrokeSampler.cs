#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Brush;

/// <summary>
/// The pure arc-length stroke sampler behind the palette's <b>multi-stamp</b> hold-drag
/// (island-authoring Slice 4 — the embryo of the future scatter brush). Separated from
/// <c>PalettePlacementSystem</c> so the spacing math is unit-testable without a world or a cursor,
/// and so there is exactly one source of truth for it.
///
/// <para>The palette tracks the world position of the <b>last stamp</b>. Each frame of a
/// hold-drag it calls <see cref="Sample"/> with that anchor and the current cursor world position;
/// the sampler returns the new stamp points spaced <paramref name="spacing"/> apart along the
/// segment (never overshooting the cursor). The caller then advances its anchor to the last
/// returned point, so the leftover fractional distance carries into the next frame — spacing is
/// exact arc-length regardless of frame rate or cursor speed, with no jitter and no seed (a plain
/// spacing brush, per the banked multi-stamp decision).</para>
/// </summary>
public static class StrokeSampler
{
    /// <summary>
    /// The stamp points along the segment <paramref name="from"/>→<paramref name="to"/>, spaced
    /// <paramref name="spacing"/> apart, strictly <b>after</b> <paramref name="from"/> (which was
    /// already stamped) and never past <paramref name="to"/>. Returns an empty list when the
    /// spacing is non-positive or the segment is shorter than one spacing (nothing new to stamp
    /// yet — the leftover distance carries to the next call).
    /// </summary>
    public static List<Vector2> Sample(Vector2 from, Vector2 to, float spacing)
    {
        var points = new List<Vector2>();
        if (spacing <= 0f) return points;

        var delta = to - from;
        var distance = delta.Length();
        if (distance < spacing) return points;

        var direction = delta / distance;
        for (var travelled = spacing; travelled <= distance + 1e-4f; travelled += spacing)
            points.Add(from + direction * travelled);
        return points;
    }
}
