#nullable enable
using System;
using DefaultEcs.System;
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

    public bool IsEnabled { get; set; } = true;

    public EditorOverlayPrepSystem(GizmoSystem gizmo, ProxySyncSystem proxySync)
    {
        _gizmo = gizmo ?? throw new ArgumentNullException(nameof(gizmo));
        _proxySync = proxySync ?? throw new ArgumentNullException(nameof(proxySync));
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        _gizmo.EmitOverlays(state);
        _proxySync.EmitOverlays(state);
    }

    public void Dispose()
    {
        // The gizmo/proxy systems own the overlay entities; the woven pipeline disposes them.
    }
}
