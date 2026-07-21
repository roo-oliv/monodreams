#nullable enable
namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The editor's per-session <b>viewport-overlay settings</b> (UX3-D §3) — pure data on a standalone
/// editor-state entity (tagged <c>EditorInfrastructureComponent</c> so it survives a transport
/// Restart). Drives the Overlays dropdown menu and the overlay emit passes: whether the world grid,
/// the selection outline, and the camera-entity glyph are shown.
///
/// <para><b>Grid spacing is deliberately NOT here.</b> The engine has ONE grid quantum — the gizmo's
/// snap step, <see cref="GizmoStateComponent.GridStep"/>. The displayed grid MUST be the grid things
/// snap to, or the overlay lies (UX3-D §3, "Grid spacing = snap step, one value"), so the grid
/// renderer, the <c>overlay:spacing</c> op, and the menu's spacing presets all read/write that single
/// authoritative field — there is no second copy on this component.</para>
///
/// <para>Session-scoped v1: NOT registered on the component-serializer registry, so it never enters a
/// <c>.mdscene</c> (per-project persistence is named terrain — see the viewport-overlays premise).</para>
/// </summary>
public struct ViewportOverlaySettingsComponent
{
    /// <summary>Whether the world-space reference grid is drawn (default <b>off</b> — preserves the
    /// current look). Spacing is <see cref="GizmoStateComponent.GridStep"/> (the shared grid quantum).</summary>
    public bool ShowGrid;

    /// <summary>Whether the selection outline is emitted around the selected entity (default <b>on</b>).
    /// Gating this off suppresses only the outline VISUAL — selection itself is unaffected.</summary>
    public bool OutlineSelected;

    /// <summary>Whether the scene camera's frustum glyph is shown (default <b>on</b>). Off hides the glyph
    /// entirely; the view/camera divergence rule (glyph shows only while they differ) applies only while
    /// this is on.</summary>
    public bool ShowCameraGlyph;

    /// <summary>The session defaults: grid OFF (current look), outline ON, camera glyph ON.</summary>
    public static ViewportOverlaySettingsComponent Default => new()
    {
        ShowGrid = false,
        OutlineSelected = true,
        ShowCameraGlyph = true,
    };
}
