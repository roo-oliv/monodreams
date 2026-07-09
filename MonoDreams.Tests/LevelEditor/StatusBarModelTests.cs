using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX3-F status-bar formatting (design §5) on the pure <see cref="StatusBarModel"/>: the
/// modal readout (mode word + live values + axis tag + typed buffer + confirm hint), the contextual
/// left status, and the right scene-id + mode. ASCII-only (the chrome font has no Δ/×/°/· glyphs).
/// </summary>
public class StatusBarModelTests
{
    private static ModalReadout GrabReadout(ModalAxis axis, string buffer, float dx, float dy) =>
        new(EditorModalMode.Grab, isRig: false, axis, buffer, dx, dy, 1f, 1f, 0f);

    // ── Left: modal readout ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LeftModal_Grab_ShowsModeDeltasAxisAndConfirmHint()
    {
        var s = StatusBarModel.LeftModal(GrabReadout(ModalAxis.X, buffer: "", dx: 12f, dy: -3.5f));
        Assert.StartsWith("Move", s);
        Assert.Contains("dX 12.0", s);
        Assert.Contains("dY -3.5", s);
        Assert.Contains("[X]", s);
        Assert.Contains("type = exact", s); // no buffer → the "exact" hint
        Assert.Contains(StatusBarModel.ConfirmHint, s);
    }

    [Fact]
    public void LeftModal_Grab_Typed_RendersTheBuffer()
    {
        var s = StatusBarModel.LeftModal(GrabReadout(ModalAxis.X, buffer: "24", dx: 24f, dy: 0f));
        Assert.Contains("type = 24", s);
        Assert.DoesNotContain("press X or Y", s); // an axis is locked
    }

    [Fact]
    public void LeftModal_Grab_TypedWithoutAxis_PromptsPressXOrY()
    {
        var s = StatusBarModel.LeftModal(GrabReadout(ModalAxis.None, buffer: "24", dx: 0f, dy: 0f));
        Assert.Contains("type = 24 (press X or Y)", s);
    }

    [Fact]
    public void LeftModal_Scale_FreeIsUniform_ConstrainedIsPerAxis_RigIsZoom()
    {
        var free = StatusBarModel.LeftModal(new ModalReadout(
            EditorModalMode.Scale, false, ModalAxis.None, "", 0, 0, 2f, 2f, 0));
        Assert.Contains("Scale  x2.0", free);

        var perAxis = StatusBarModel.LeftModal(new ModalReadout(
            EditorModalMode.Scale, false, ModalAxis.X, "", 0, 0, 2f, 1f, 0));
        Assert.Contains("X x2.0", perAxis);
        Assert.Contains("Y x1.0", perAxis);

        var rig = StatusBarModel.LeftModal(new ModalReadout(
            EditorModalMode.Scale, isRig: true, ModalAxis.None, "", 0, 0, 1.5f, 1.5f, 0));
        Assert.Contains("Zoom  x1.5", rig);
    }

    [Fact]
    public void LeftModal_Rotate_ShowsDegrees()
    {
        var s = StatusBarModel.LeftModal(new ModalReadout(
            EditorModalMode.Rotate, false, ModalAxis.None, "", 0, 0, 1f, 1f, 45f));
        Assert.StartsWith("Rotate", s);
        Assert.Contains("45.0 deg", s);
    }

    // ── Left: contextual status ──────────────────────────────────────────────────────────────────

    [Fact]
    public void LeftStatus_NoSelection_AndCountPluralization()
    {
        Assert.Equal("No selection  |  0 entities", StatusBarModel.LeftStatus(null, 0));
        Assert.Equal("Tree  |  1 entity", StatusBarModel.LeftStatus("Tree", 1));
        Assert.Equal("Player  |  12 entities", StatusBarModel.LeftStatus("Player", 12));
    }

    // ── Right: scene id + mode ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Right_ShowsSceneIdAndMode()
    {
        Assert.Equal("island2  |  Scene mode", StatusBarModel.Right("island2", EditorViewMode.Scene));
        Assert.Equal("island2  |  Game mode", StatusBarModel.Right("island2", EditorViewMode.Game));
        Assert.Equal("Scene mode", StatusBarModel.ModeLabel(EditorViewMode.Scene));
        Assert.Equal("Game mode", StatusBarModel.ModeLabel(EditorViewMode.Game));
    }
}
