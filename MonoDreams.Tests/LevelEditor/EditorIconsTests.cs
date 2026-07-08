using System;
using Microsoft.Xna.Framework;
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
}
