using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using Xunit;
using static MonoDreams.LevelEditor.UI.EditorIcons;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX2-C icon library (<see cref="EditorIcons"/>): the pure geometry stays inside its
/// rect, mirrored glyphs (Undo/Redo, Restart/Refresh) are exact horizontal reflections, DPR scaling is
/// pure rect scaling, and the action→icon / active-tool mappings that drive the toolbar are correct. No
/// GraphicsDevice — the icons are line/triangle lists in a unit box.
/// </summary>
public class EditorIconsTests
{
    private static readonly EditorIcon[] AllIcons = (EditorIcon[])Enum.GetValues(typeof(EditorIcon));

    private static readonly Color Ink = new(200, 100, 50); // any non-theme test colour is fine here

    [Fact]
    public void EveryIcon_GeometryStaysInsideTheGivenRect()
    {
        var rect = new Rectangle(40, 25, 100, 100);
        const float tol = 1.0f; // absorb float rounding at stroke extremes; the shapes sit well inside
        foreach (var icon in AllIcons)
        {
            var mesh = Build(icon, rect, Ink);
            Assert.True(mesh.Vertices.Length > 0, $"{icon} produced no geometry");
            foreach (var v in mesh.Vertices)
            {
                Assert.InRange(v.Position.X, rect.Left - tol, rect.Right + tol);
                Assert.InRange(v.Position.Y, rect.Top - tol, rect.Bottom + tol);
            }
        }
    }

    [Fact]
    public void EveryIcon_BakesTheGivenColorIntoEveryVertex()
    {
        var rect = new Rectangle(0, 0, 64, 64);
        foreach (var icon in AllIcons)
            foreach (var v in Build(icon, rect, Ink).Vertices)
                Assert.Equal(Ink, v.Color);
    }

    [Theory]
    [InlineData(EditorIcon.Undo, EditorIcon.Redo)]
    [InlineData(EditorIcon.Restart, EditorIcon.Refresh)]
    public void MirroredIcons_AreExactHorizontalReflections(EditorIcon left, EditorIcon right)
    {
        var rect = new Rectangle(0, 0, 100, 100);
        var a = Build(left, rect, Ink).Vertices;
        var b = Build(right, rect, Ink).Vertices;
        Assert.Equal(a.Length, b.Length);

        // Every vertex of `left`, reflected about the rect's vertical centre line, must appear in `right`
        // (same Y). Reflection is algebraic (K - x), so a small epsilon covers float noise only.
        var mid = rect.Left + rect.Right;
        const float eps = 0.5f;
        foreach (var v in a)
        {
            var rx = mid - v.Position.X;
            var found = false;
            foreach (var w in b)
                if (MathF.Abs(w.Position.X - rx) < eps && MathF.Abs(w.Position.Y - v.Position.Y) < eps)
                {
                    found = true;
                    break;
                }
            Assert.True(found, $"{left}/{right} not mirrored: ({rx},{v.Position.Y}) missing");
        }

        // And they are NOT identical (a real mirror, not a symmetric no-op).
        var different = false;
        for (var i = 0; i < a.Length; i++)
            if (MathF.Abs(a[i].Position.X - b[i].Position.X) > eps) { different = true; break; }
        Assert.True(different, $"{left} and {right} are identical — the mirror did nothing");
    }

    [Fact]
    public void DprScaling_IsPureRectScaling()
    {
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(0, 0, 200, 200); // exact 2× from the origin
        const float eps = 0.01f;
        foreach (var icon in AllIcons)
        {
            var v1 = Build(icon, r1, Ink).Vertices;
            var v2 = Build(icon, r2, Ink).Vertices;
            Assert.Equal(v1.Length, v2.Length);
            for (var i = 0; i < v1.Length; i++)
            {
                Assert.True(MathF.Abs(v2[i].Position.X - 2 * v1[i].Position.X) < eps, $"{icon} X not 2×");
                Assert.True(MathF.Abs(v2[i].Position.Y - 2 * v1[i].Position.Y) < eps, $"{icon} Y not 2×");
            }
        }
    }

    [Fact]
    public void CenteredIconRect_IsCenteredAndDoublesWithTheButton()
    {
        var small = CenteredIconRect(new Rectangle(10, 20, 30, 30));
        var large = CenteredIconRect(new Rectangle(0, 0, 60, 60));

        // Centered inside the button (within 1px of the button centre).
        Assert.InRange(small.Center.X, 25 - 1, 25 + 1); // button centre X = 10 + 15
        Assert.InRange(small.Center.Y, 35 - 1, 35 + 1); // button centre Y = 20 + 15
        Assert.Equal(small.Width, small.Height);        // square

        // A 2× button gives ~2× the glyph square (rounding tolerance).
        Assert.InRange(large.Width, 2 * small.Width - 2, 2 * small.Width + 2);
        // The glyph is a sensible fraction of the button (not the whole button, not a speck).
        Assert.InRange(small.Width, 15, 24); // ~0.62 × 30
    }

    [Fact]
    public void ForAction_MapsIconActions_TextActionsHaveNoIcon()
    {
        // Transport + tools + Save/Undo/Redo/Refresh have icons…
        Assert.NotNull(ForAction(EditorToolbarAction.PlayPause));
        Assert.NotNull(ForAction(EditorToolbarAction.Restart));
        Assert.Equal(EditorIcon.Move, ForAction(EditorToolbarAction.ToolMove));
        Assert.Equal(EditorIcon.Rotate, ForAction(EditorToolbarAction.ToolRotate));
        Assert.Equal(EditorIcon.Scale, ForAction(EditorToolbarAction.ToolScale));
        Assert.Equal(EditorIcon.Boundary, ForAction(EditorToolbarAction.ToolBoundary));
        Assert.Equal(EditorIcon.Snap, ForAction(EditorToolbarAction.ToggleSnap));
        Assert.Equal(EditorIcon.Save, ForAction(EditorToolbarAction.Save));
        Assert.Equal(EditorIcon.Undo, ForAction(EditorToolbarAction.Undo));
        Assert.Equal(EditorIcon.Redo, ForAction(EditorToolbarAction.Redo));
        Assert.Equal(EditorIcon.Refresh, ForAction(EditorToolbarAction.RefreshCatalog));

        // …the selection-context actions stay text (no icon this wave).
        Assert.Null(ForAction(EditorToolbarAction.OrderForward));
        Assert.Null(ForAction(EditorToolbarAction.OrderBack));
        Assert.Null(ForAction(EditorToolbarAction.ColliderAddBox));
        Assert.Null(ForAction(EditorToolbarAction.ColliderAddConvex));
        Assert.Null(ForAction(EditorToolbarAction.ColliderRemove));
        Assert.Null(ForAction(EditorToolbarAction.VertexAdd));
        Assert.False(HasIcon(EditorToolbarAction.OrderForward));
        Assert.True(HasIcon(EditorToolbarAction.Save));
    }

    [Fact]
    public void Resolve_SwapsPlayPauseByTransportState()
    {
        Assert.Equal(EditorIcon.Play, Resolve(EditorToolbarAction.PlayPause, playing: false));
        Assert.Equal(EditorIcon.Pause, Resolve(EditorToolbarAction.PlayPause, playing: true));
        // Non-transport icons are state-independent.
        Assert.Equal(EditorIcon.Save, Resolve(EditorToolbarAction.Save, playing: true));
    }

    [Fact]
    public void IsActiveIn_TracksTheRadioToolsAndSnapToggle()
    {
        var gizmo = GizmoStateComponent.Default; // SelectTransform, Move tool, snap off
        Assert.True(EditorToolbarAction.ToolMove.IsActiveIn(gizmo));
        Assert.False(EditorToolbarAction.ToolRotate.IsActiveIn(gizmo));
        Assert.False(EditorToolbarAction.ToolBoundary.IsActiveIn(gizmo));
        Assert.False(EditorToolbarAction.ToggleSnap.IsActiveIn(gizmo));

        gizmo.Tool = GizmoTool.Rotate;
        Assert.False(EditorToolbarAction.ToolMove.IsActiveIn(gizmo));
        Assert.True(EditorToolbarAction.ToolRotate.IsActiveIn(gizmo));

        // In a non-SelectTransform modality no transform tool is the active radio…
        gizmo.Mode = EditorToolMode.Boundary;
        Assert.False(EditorToolbarAction.ToolRotate.IsActiveIn(gizmo));
        Assert.True(EditorToolbarAction.ToolBoundary.IsActiveIn(gizmo));

        gizmo.SnapEnabled = true;
        Assert.True(EditorToolbarAction.ToggleSnap.IsActiveIn(gizmo));
    }

    // ── UX3-C icon polish (the user's hands-on feedback + reference images) ────────────────────────────
    //
    // These tests segment the flat triangle-list mesh back into the PRIMITIVES that authored it: a thick
    // line and a filled quad are each 4 vertices + 6 indices in the pattern [o,o+1,o+2, o,o+2,o+3], while
    // a filled triangle (an arrowhead, or the video-camera lens) is 3 vertices + 3 indices. That lets a
    // pure test assert on real glyph geometry — the arrowhead extent, the closed grid, the video-camera
    // silhouette, the beveled floppy — in coordinates relative to the icon box.

    /// <summary>The side of the unit-friendly box the geometry tests build in, so a pixel value ÷ Side is
    /// the unit-box fraction the glyph was authored in.</summary>
    private const int Side = 100;

    /// <summary>A primitive recovered from the flat mesh: a filled triangle (<see cref="Tri"/> = true, 3
    /// verts) or a quad (false, 4 verts — a thick line OR a filled rectangle). Verts are unit-box coords.</summary>
    private readonly record struct Prim(bool Tri, Vector2[] V);

    private static List<Prim> Primitives(MeshData mesh)
    {
        var prims = new List<Prim>();
        var idx = mesh.Indices;
        Vector2 P(int i) => new(mesh.Vertices[i].Position.X / Side, mesh.Vertices[i].Position.Y / Side);
        var k = 0;
        while (k < idx.Length)
        {
            // A quad emits [o,o+1,o+2, o,o+2,o+3]; anything else is a standalone triangle.
            if (k + 5 < idx.Length &&
                idx[k] == idx[k + 3] && idx[k + 2] == idx[k + 4] &&
                idx[k + 1] == idx[k] + 1 && idx[k + 2] == idx[k] + 2 && idx[k + 5] == idx[k] + 3)
            {
                var o = idx[k];
                prims.Add(new Prim(false, new[] { P(o), P(o + 1), P(o + 2), P(o + 3) }));
                k += 6;
            }
            else
            {
                prims.Add(new Prim(true, new[] { P(idx[k]), P(idx[k + 1]), P(idx[k + 2]) }));
                k += 3;
            }
        }
        return prims;
    }

    /// <summary>Shortest side of a primitive — a thick line's is the stroke thickness, a filled
    /// rectangle's is <c>min(width,height)</c> — so it separates line strokes from filled plates.</summary>
    private static float MinEdge(Vector2[] v)
    {
        var m = float.MaxValue;
        for (var i = 0; i < v.Length; i++) m = MathF.Min(m, Vector2.Distance(v[i], v[(i + 1) % v.Length]));
        return m;
    }

    /// <summary>Longest distance between any two of a triangle's vertices — its widest span, which is
    /// rotation-invariant, so it measures an arrowhead's extent regardless of the head's direction.</summary>
    private static float Extent(Vector2[] v)
    {
        var m = 0f;
        for (var i = 0; i < v.Length; i++)
            for (var j = i + 1; j < v.Length; j++) m = MathF.Max(m, Vector2.Distance(v[i], v[j]));
        return m;
    }

    /// <summary>A thick line's represented segment: the midpoints of its two short caps.</summary>
    private static (Vector2 a, Vector2 b) Segment(Vector2[] q) => ((q[0] + q[1]) / 2f, (q[2] + q[3]) / 2f);

    private static (Vector2 center, float w, float h) FillBox(Vector2[] q)
    {
        float minX = q.Min(p => p.X), maxX = q.Max(p => p.X), minY = q.Min(p => p.Y), maxY = q.Max(p => p.Y);
        return (new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f), maxX - minX, maxY - minY);
    }

    private static bool Near(float a, float b, float eps = 0.03f) => MathF.Abs(a - b) < eps;

    // (1) "The arrow pointers (triangles) can be made a little more prominent" → every filled arrowhead is
    // ≥22% of the icon box, on Move (all 4 heads), Rotate, Scale (both heads), Undo/Redo, Restart/Refresh.
    [Theory]
    [InlineData(EditorIcon.Move, 4)]
    [InlineData(EditorIcon.Scale, 2)]
    [InlineData(EditorIcon.Rotate, 1)]
    [InlineData(EditorIcon.Undo, 1)]
    [InlineData(EditorIcon.Redo, 1)]
    [InlineData(EditorIcon.Restart, 1)]
    [InlineData(EditorIcon.Refresh, 1)]
    public void Arrowheads_AreProminent_AtLeast22PercentOfTheBox(EditorIcon icon, int expectedHeads)
    {
        var heads = Primitives(Build(icon, new Rectangle(0, 0, Side, Side), Ink))
            .Where(p => p.Tri).Select(p => p.V).ToList();
        Assert.Equal(expectedHeads, heads.Count); // Move keeps all 4 heads, Scale both, the rest exactly one
        foreach (var head in heads)
            Assert.True(Extent(head) >= 0.22f, $"{icon} arrowhead extent {Extent(head):F3} < 0.22 of the box");
    }

    // (2) "The Snap to grid symbol looks more like a hashtag than a grid, draw a square to make the borders
    // closed" → a closed square border (4 strokes) + an inner 3×3 grid (2 verticals + 2 horizontals).
    [Fact]
    public void Snap_IsAClosedSquareBorder_WithAnInner3x3Grid()
    {
        var prims = Primitives(Build(EditorIcon.Snap, new Rectangle(0, 0, Side, Side), Ink));
        Assert.All(prims, p => Assert.False(p.Tri));   // every stroke is a line — no fills, no triangles
        Assert.Equal(8, prims.Count);                  // 4 border + 4 inner

        var segs = prims.Select(p => Segment(p.V)).ToList();
        float U((Vector2 a, Vector2 b) s) => (s.a.X + s.b.X) / 2f;
        float Vc((Vector2 a, Vector2 b) s) => (s.a.Y + s.b.Y) / 2f;
        var verticals = segs.Where(s => Near(s.a.X, s.b.X, 0.02f)).ToList();
        var horizontals = segs.Where(s => Near(s.a.Y, s.b.Y, 0.02f)).ToList();
        Assert.Equal(4, verticals.Count);
        Assert.Equal(4, horizontals.Count);

        float minU = segs.Min(s => MathF.Min(s.a.X, s.b.X)), maxU = segs.Max(s => MathF.Max(s.a.X, s.b.X));
        float minV = segs.Min(s => MathF.Min(s.a.Y, s.b.Y)), maxV = segs.Max(s => MathF.Max(s.a.Y, s.b.Y));
        bool FullV((Vector2 a, Vector2 b) s) => Near(MathF.Min(s.a.Y, s.b.Y), minV) && Near(MathF.Max(s.a.Y, s.b.Y), maxV);
        bool FullU((Vector2 a, Vector2 b) s) => Near(MathF.Min(s.a.X, s.b.X), minU) && Near(MathF.Max(s.a.X, s.b.X), maxU);

        // The closed square: a full-height vertical on each side + a full-width horizontal top and bottom.
        Assert.True(verticals.Any(s => Near(U(s), minU) && FullV(s)), "no closed left border");
        Assert.True(verticals.Any(s => Near(U(s), maxU) && FullV(s)), "no closed right border");
        Assert.True(horizontals.Any(s => Near(Vc(s), minV) && FullU(s)), "no closed top border");
        Assert.True(horizontals.Any(s => Near(Vc(s), maxV) && FullU(s)), "no closed bottom border");

        // The inner 3×3 lines: 2 verticals + 2 horizontals strictly inside, at the thirds.
        var innerV = verticals.Where(s => U(s) > minU + 0.05f && U(s) < maxU - 0.05f).ToList();
        var innerH = horizontals.Where(s => Vc(s) > minV + 0.05f && Vc(s) < maxV - 0.05f).ToList();
        Assert.Equal(2, innerV.Count);
        Assert.Equal(2, innerH.Count);
        Assert.True(innerV.Any(s => Near(U(s), minU + (maxU - minU) / 3f)), "no inner vertical at 1/3");
        Assert.True(innerV.Any(s => Near(U(s), minU + 2f * (maxU - minU) / 3f)), "no inner vertical at 2/3");
    }

    // (3) "The camera view icon is too hard to understand, make it more obvious" → the classic
    // video-camera: a body rect (left ~55–60%) + a top tab + a triangle whose apex touches the body edge.
    [Fact]
    public void Camera_IsAVideoCamera_BodyRectPlusTopTabPlusTriangleApexOnTheBodyEdge()
    {
        var prims = Primitives(Build(EditorIcon.Camera, new Rectangle(0, 0, Side, Side), Ink));

        // Exactly one filled triangle — the lens / tape cone: apex on the left (the body edge), base right.
        var lens = Assert.Single(prims.Where(p => p.Tri).Select(p => p.V));
        var apex = lens.OrderBy(pt => pt.X).First();
        var baseP = lens.OrderByDescending(pt => pt.X).Take(2).ToArray();
        Assert.All(baseP, pt => Assert.True(pt.X > 0.80f, "lens base is not on the far right"));
        Assert.InRange(apex.X, 0.60f, 0.72f);                                              // apex at body right edge
        var baseMidY = (baseP[0].Y + baseP[1].Y) / 2f;
        Assert.InRange(apex.Y, baseMidY - 0.04f, baseMidY + 0.04f);                        // apex vertically centred

        // The body: a vertical edge at the apex's u (the edge the apex TOUCHES) and one on the left, both
        // spanning the body height; the gap between them is the left ~55–60% of the box.
        float U((Vector2 a, Vector2 b) s) => (s.a.X + s.b.X) / 2f;
        float VSpan((Vector2 a, Vector2 b) s) => MathF.Abs(s.a.Y - s.b.Y);
        var verticals = prims.Where(p => !p.Tri).Select(p => Segment(p.V))
                             .Where(s => Near(s.a.X, s.b.X, 0.02f)).ToList();
        var bodyRight = verticals.Where(s => Near(U(s), apex.X, 0.04f) && VSpan(s) > 0.25f).ToList();
        var bodyLeft = verticals.Where(s => U(s) < 0.18f && VSpan(s) > 0.25f).ToList();
        Assert.NotEmpty(bodyRight);   // the apex touches the body's right edge
        Assert.NotEmpty(bodyLeft);
        Assert.InRange(U(bodyRight[0]) - U(bodyLeft[0]), 0.50f, 0.62f);

        // The top tab/handle: line geometry above the body's top edge.
        Assert.Contains(prims.Where(p => !p.Tri).SelectMany(p => p.V), v => v.Y < 0.30f);
    }

    // (4) "The save icon can be improved, with a bevel on the top right corner and the top rectangle
    // displaced a little to the left" → the classic floppy.
    [Fact]
    public void Save_IsAFloppy_BeveledTopRight_ShutterLeftOfCentre_LabelCentred()
    {
        var prims = Primitives(Build(EditorIcon.Save, new Rectangle(0, 0, Side, Side), Ink));

        // Two filled plates — the shutter (top) and the label (bottom); everything else is a thin stroke.
        var fills = prims.Where(p => !p.Tri && MinEdge(p.V) > 0.15f).Select(p => FillBox(p.V))
                        .OrderBy(f => f.center.Y).ToList();
        Assert.Equal(2, fills.Count);
        var shutter = fills[0];
        var label = fills[1];
        Assert.True(shutter.center.X < 0.5f - 0.03f, $"shutter centre-x {shutter.center.X:F3} not left of centre");
        Assert.InRange(label.center.X, 0.5f - 0.03f, 0.5f + 0.03f);   // label centred

        // Beveled top-RIGHT corner: the top edge stops short of the right AND the right edge starts low —
        // only a diagonal corner cut vacates the extreme top-right corner both ways.
        var verts = prims.SelectMany(p => p.V).ToList();
        Assert.True(verts.Where(v => v.Y < 0.24f).Max(v => v.X) < 0.75f, "top edge reaches the right — not beveled");
        Assert.True(verts.Where(v => v.X > 0.75f).Min(v => v.Y) > 0.24f, "right edge starts at the top — not beveled");
    }
}
