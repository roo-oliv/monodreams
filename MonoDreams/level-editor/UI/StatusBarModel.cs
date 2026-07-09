#nullable enable
using System.Globalization;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// The pure formatter for the window <b>status bar</b> (UX3-F design §5, "like Blender and IntelliJ").
/// It turns injected state into the two label strings the <c>EditorStatusBarSystem</c> renders — so the
/// content is unit-testable without a world, a font, or a GraphicsDevice.
///
/// <para><b>Left.</b> While a modal transform is active, the live readout (<see cref="LeftModal"/>):
/// the mode word, the live values (ΔX/ΔY for grab, a factor for scale, degrees for rotate), the axis
/// tag when locked, and the numeric buffer as typed — ending with the confirm/cancel hint. Otherwise
/// the contextual status (<see cref="LeftStatus"/>): the selected entity's name (or "No selection")
/// and the scene-entity count.</para>
///
/// <para><b>Right (PF-B).</b> The ACTIVE viewport tab's id and — on the Game tab — its transport state
/// (<see cref="Right"/>): <c>island</c> on the Scene tab, <c>island | Playing</c> / <c>island | Paused</c>
/// on the Game tab (the run state replaces the retired "Scene mode" / "Game mode" words — the tab strip
/// now shows which context is active). The dirty marker is a <see cref="EditorTheme.Warning"/>-colored dot
/// the system draws as a MESH (the bitmap font has no bullet glyph), gated on the injected dirty state — it
/// is not part of these strings.</para>
///
/// <para><b>ASCII only.</b> The chrome's bitmap font (PPMondwest) has no <c>Δ</c> / <c>×</c> / <c>°</c> /
/// <c>·</c> / <c>●</c> glyphs, so the readout uses <c>dX</c>/<c>dY</c>, <c>x</c> for the scale factor,
/// <c>deg</c>, and <c>|</c> separators — and the dirty dot is a mesh, not a glyph.</para>
/// </summary>
public static class StatusBarModel
{
    /// <summary>The confirm/cancel hint that tails every modal readout.</summary>
    public const string ConfirmHint = "LMB/Enter confirm | RMB/Esc cancel";

    /// <summary>
    /// The left readout while a modal transform is active. Format:
    /// <c>&lt;mode&gt;  &lt;values&gt;  [axis]  type = &lt;buffer|exact&gt;  |  LMB/Enter confirm | RMB/Esc cancel</c>.
    /// A typed-but-unconstrained grab appends "(press X or Y)" — the SIMPLIFY-v1 rule that a typed grab
    /// requires an axis. The scale word is "Zoom" for the camera rig.
    /// </summary>
    public static string LeftModal(in ModalReadout r)
    {
        var body = r.Mode switch
        {
            EditorModalMode.Grab => $"Move  dX {F(r.DX)}  dY {F(r.DY)}",
            EditorModalMode.Scale => ScaleBody(r),
            EditorModalMode.Rotate => $"Rotate  {F(r.Degrees)} deg",
            _ => "Modal",
        };

        var axisTag = r.Axis switch
        {
            ModalAxis.X => "  [X]",
            ModalAxis.Y => "  [Y]",
            _ => string.Empty,
        };

        var typed = !string.IsNullOrEmpty(r.Buffer);
        var typeSeg = $"type = {(typed ? r.Buffer : "exact")}";
        // The SIMPLIFY-v1 grab rule: a typed value only applies along a locked axis — prompt otherwise.
        if (typed && r.Mode == EditorModalMode.Grab && r.Axis == ModalAxis.None)
            typeSeg += " (press X or Y)";

        return $"{body}{axisTag}  {typeSeg}  |  {ConfirmHint}";
    }

    private static string ScaleBody(in ModalReadout r)
    {
        var word = r.IsRig ? "Zoom" : "Scale";
        return r.Axis == ModalAxis.None
            ? $"{word}  x{F(r.FactorX)}"
            : $"{word}  X x{F(r.FactorX)}  Y x{F(r.FactorY)}";
    }

    /// <summary>The contextual left status when no modal is active: the selection name (or
    /// "No selection") and the scene-entity count.</summary>
    public static string LeftStatus(string? selectedName, int entityCount)
    {
        var name = string.IsNullOrEmpty(selectedName) ? "No selection" : selectedName!;
        var noun = entityCount == 1 ? "entity" : "entities";
        return $"{name}  |  {entityCount} {noun}";
    }

    /// <summary>The right side (PF-B): the ACTIVE viewport tab's id, plus its transport state on the
    /// <see cref="ViewportContextKind.Game"/> tab (<c>Playing</c> / <c>Paused</c>). The Scene tab (and a
    /// future Prefab tab, which never plays) show the id alone — the tab strip already names the active
    /// context, so the run state is only meaningful for the Game sandbox. The dirty dot (a Warning mesh)
    /// is drawn separately by the system, gated on the injected dirty state.</summary>
    public static string Right(string tabId, ViewportContextKind activeKind, RunMode runMode) =>
        activeKind == ViewportContextKind.Game
            ? $"{tabId}  |  {(runMode == RunMode.Play ? "Playing" : "Paused")}"
            : tabId;

    private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
