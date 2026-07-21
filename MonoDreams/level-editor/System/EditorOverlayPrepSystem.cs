#nullable enable
using System;
using DefaultEcs.System;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor overlays' draw-phase emission pass (registrar entry <c>editor.overlayPrep</c>): it
/// invokes <see cref="GizmoSystem.EmitOverlays"/> and <see cref="ProxySyncSystem.EmitOverlays"/>,
/// which bake the frame's overlay VISUALS — selection outline, active-tool handle, collider-proxy
/// outlines — in screen pixels on the native-resolution <c>RenderTargetID.Editor</c> target (see
/// <c>OverlayProjection</c>).
///
/// <para><b>Why a draw-phase pass.</b> The gizmo and proxy-sync systems run in the UPDATE
/// pipeline <b>before</b> <c>editor.cameraNav</c> (their ordering contract with
/// <c>HierarchySystem</c>) and before the draw-side <c>SelectionSystem</c>. Screen-baked
/// geometry emitted there would use the pre-pan/zoom camera and the previous frame's selection —
/// a one-frame swim of the overlays against the world during navigation, and a one-frame-late
/// outline after a click. Emitting here — woven <b>after</b> <c>editor.selection</c> and before
/// the render passes — reads the frame's FINAL camera and selection, exactly like the game's own
/// prep systems freeze their state at the tail of the frame (CORE_TENETS: rendering runs
/// last).</para>
///
/// <para>The pass owns no state of its own (the gizmo/proxy systems own their overlay entities);
/// disabling it in the systems panel freezes the overlay visuals in place. It is registered
/// RunNormally and self-guards through the emit methods' Edit checks (they hide/skip outside
/// Edit).</para>
/// </summary>
public sealed class EditorOverlayPrepSystem : ISystem<GameState>
{
    private readonly GizmoSystem _gizmo;
    private readonly ProxySyncSystem _proxySync;
    private readonly BoundaryToolSystem? _boundary;
    private readonly TriggerOverlaySystem? _triggers;
    private readonly CameraEntityOverlay? _cameraOverlay;
    private readonly EditorGrid? _grid;

    public bool IsEnabled { get; set; } = true;

    /// <param name="boundary">Optional boundary tool (island-authoring Slice 3): its
    /// <see cref="BoundaryToolSystem.EmitOverlays"/> bakes committed boundary outlines + the lay
    /// preview into this same pass.</param>
    /// <param name="triggers">Optional trigger overlay (Slice 3): its
    /// <see cref="TriggerOverlaySystem.EmitOverlays"/> bakes trigger-zone outlines + the placement
    /// ghost.</param>
    /// <param name="cameraOverlay">Optional camera-entity overlay (CM): its
    /// <see cref="CameraEntityOverlay.EmitGlyph"/> bakes the scene camera's frustum glyph (bounds + X)
    /// into this same pass when the view differs from the camera entity.</param>
    /// <param name="grid">Optional world-space grid (UX3-D): its <see cref="EditorGrid.EmitGrid"/> bakes
    /// the reference grid into this same pass, BENEATH the other overlays (lowest overlay depth).</param>
    public EditorOverlayPrepSystem(GizmoSystem gizmo, ProxySyncSystem proxySync,
        BoundaryToolSystem? boundary = null, TriggerOverlaySystem? triggers = null,
        CameraEntityOverlay? cameraOverlay = null, EditorGrid? grid = null)
    {
        _gizmo = gizmo ?? throw new ArgumentNullException(nameof(gizmo));
        _proxySync = proxySync ?? throw new ArgumentNullException(nameof(proxySync));
        _boundary = boundary;
        _triggers = triggers;
        _cameraOverlay = cameraOverlay;
        _grid = grid;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        // Grid first — the backdrop reference beneath the interactive overlays (depth-ordered anyway).
        _grid?.EmitGrid(state);
        _gizmo.EmitOverlays(state);
        _proxySync.EmitOverlays(state);
        _boundary?.EmitOverlays(state);
        _triggers?.EmitOverlays(state);
        _cameraOverlay?.EmitGlyph(state);
    }

    public void Dispose()
    {
        // The gizmo/proxy systems own the overlay entities; the woven pipeline disposes them.
    }
}
