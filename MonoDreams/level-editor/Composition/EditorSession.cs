#nullable enable

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's <b>host-scoped session</b> (TB-A): one per host, constructed once in each host's
/// <c>Game1</c> beside the <c>ScreenController</c> and passed to every editor-enabled screen exactly like
/// the project context. It owns the <see cref="ViewportContextStack"/> (the ordered viewport tab
/// descriptors + the active tab + the per-tab data snapshots) and the pending-activation slot — the state
/// that must survive a screen switch, mirroring how the shared <c>GameState.RunMode</c> survives one.
///
/// <para><b>Why host-scoped.</b> Before TB-A the tab machinery lived on the per-screen
/// <see cref="EditorOverlay"/>, so a gameplay <c>LoadScreen</c> disposed the world, the overlay, and the
/// whole tab stack — the Game tab and every scene snapshot vanished mid-transition. Hoisting the stack to
/// a host-scoped session keeps the tabs alive across the switch: the new screen's overlay BINDS to the
/// same session (<see cref="EditorOverlay"/> rebinds the stack's per-screen deps through the transport)
/// and either follows the still-active Game tab (gameplay owns the world — no restore) or consumes a
/// <see cref="PendingActivation"/> to restore a cross-screen scene tab. Overlay disposal detaches but
/// never destroys the session.</para>
///
/// <para><b>Contexts hold DATA only</b> (SceneData snapshots, view, dirty/save-point, scene id, screen
/// name) — never live World/Entity refs across screens (pre-mortem #1), so a context restored on a
/// different screen instance rebuilds cleanly through the reader.</para>
/// </summary>
public sealed class EditorSession
{
    /// <summary>The host-scoped viewport tab stack (the ONE tab-switching mechanism). Created here so its
    /// context list survives every screen switch; the per-screen history/shell + seams are (re)bound by
    /// each overlay via <see cref="ViewportContextStack.Rebind"/>.</summary>
    public ViewportContextStack Stack { get; }

    /// <summary>
    /// A cross-screen scene-tab activation is in flight (TB-A): set by the overlay BEFORE it invokes the
    /// host <c>LoadScreen</c> hand-off, and consumed EXACTLY ONCE by the next screen's overlay in
    /// <c>Load</c> — which restores the (already-active) target scene context through the reader instead of
    /// duplicating the screen's fresh load (pre-mortem #2). A plain gameplay transition (the Game tab
    /// following a <c>ScreenTransitionRequest</c>) leaves this <c>false</c>, so the new screen keeps its
    /// fresh load and stays Playing (pre-mortem #3).
    /// </summary>
    public bool PendingActivation { get; set; }

    /// <param name="bootSceneId">The boot scene id seeding the first tab; the first overlay's
    /// <c>SetSceneId</c> corrects it to the real scene the boot screen loads.</param>
    /// <param name="bootScreenName">The boot screen's name (or null — learnt when the first overlay binds
    /// the Scenes catalog).</param>
    public EditorSession(string? bootSceneId = null, string? bootScreenName = null)
    {
        Stack = new ViewportContextStack(bootSceneId ?? EditorOverlay.DefaultSceneId, bootScreenName);
    }
}
