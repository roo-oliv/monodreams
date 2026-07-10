#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's <b>transport</b>: the one owner of <see cref="GameState.RunMode"/> under the editor run
/// configuration, and the DRIVER of the <see cref="ViewportContextStack"/> (PF-B — the ONE
/// context-switching mechanism; the transport keeps ONE owner of the live RunMode and drives the stack
/// for the tab lifecycle, rather than holding a parallel Game-mode snapshot path). With the editor
/// composed, the shell and chrome are ALWAYS visible — no key toggles the editor away — and the designer
/// drives the game like a media player:
///
/// <list type="bullet">
///   <item><b>Paused</b> = <see cref="RunMode.Edit"/> — the game logic is Freeze-gated (holds
///   still), the editing tools (selection / gizmo / camera-nav / delete-undo-redo) are live.</item>
///   <item><b>Playing</b> = <see cref="RunMode.Play"/> — the game runs inside the inset viewport,
///   the shell stays composed (transport + systems panel remain interactive), and the editing
///   tools are inert: a click in the viewport belongs to the game while playing.</item>
///   <item><b>Restart</b> — return the world to the state of the ORIGINAL load: clear the undo
///   history (its entries reference entities about to die), <see cref="ViewportContextStack.ResetToScene"/>
///   (drop any Game tab, land on the Scene tab, forget the in-memory snapshot), remove the world-level
///   level components (<see cref="CurrentLevelComponent"/> / <see cref="CurrentBackgroundColorComponent"/> —
///   the LDtk parsers subscribe to the component <i>added</i> event, so a re-publish over a
///   still-set component would never re-parse), dispose every scene entity, re-run the screen's
///   recorded <see cref="Reload"/>, and land <b>Paused</b> (also when restarted mid-Play). <b>Unsaved live
///   edits are DISCARDED</b> — the standard play-mode trade-off; Save first if you want to keep them.</item>
/// </list>
///
/// <para><b>The Scene / Game tabs (PF-B).</b> The Scene/Game mode toggle is retired; the viewport is a
/// tab strip driven by the <see cref="ContextStack"/>. <b>Play from the Scene tab spawns (or resumes) the
/// Game tab</b>: <see cref="Play"/> in the Scene context calls <see cref="EnterGameMode"/> — which
/// snapshots the Scene context BEFORE the RunMode flip (pre-mortem #7), adopts the game-camera view, and
/// pushes a discard Game tab keeping the live world as the sandbox — then flips RunMode to Play.
/// <b>Leaving the Game tab</b> (<see cref="ExitToSceneMode"/> / its <c>×</c> / a scene switch) discards the
/// sandbox and restores the Scene context (the Game tab disappears — it never persists in the background),
/// landing Paused. <see cref="ActiveContextKind"/> (the active tab's kind) supersedes the retired
/// <c>ViewMode</c> as the ONE mode signal.</para>
///
/// <para><b>The restart boundary.</b> The engine has no entity↔level association, so the boundary
/// is exclusion by editor markers: an entity survives the sweep when it carries
/// <see cref="EditorInfrastructureComponent"/> (every editor-owned entity is tagged at creation),
/// when it is the cursor pipeline (<see cref="CursorControllerComponent"/> /
/// <see cref="CursorInputComponent"/> — screen input infrastructure created once in
/// <c>Load</c>, not scene content), or when the screen's <see cref="KeepAlive"/> predicate names it
/// (screen infrastructure a system created at construction and caches by reference, e.g. the
/// dialogue UI root) — keeps propagate DOWN the <see cref="ChildOfComponent"/> chain, so naming a
/// root keeps its sub-graph. Everything else is scene content and is disposed; the
/// <see cref="Reload"/> re-creates it from the original load request. The SAME sweep
/// (<see cref="DisposeSceneEntities"/>) is what the <see cref="ContextStack"/> runs on every tab switch.</para>
///
/// <para>Not a per-frame system: transport actions are event-driven (a toolbar click, a headless
/// <c>Play</c>/<c>Pause</c>/<c>Restart</c> op), so this is plain composition infrastructure like
/// <see cref="Undo.EditorHistory"/>, held by the <see cref="EditorOverlay"/>.</para>
/// </summary>
public sealed class EditorTransport
{
    /// <summary>Guards the KeepAlive ancestor walk against a malformed ChildOf cycle.</summary>
    private const int MaxParentWalk = 64;

    private readonly World _world;
    private readonly EditorHistory _history;
    private readonly ViewportContextStack _stack;

    /// <summary>Optional seam the transport routes a dirty prefab tab's <c>×</c> through (pre-mortem #9 —
    /// the Save &amp; Close / Discard / Cancel confirm). Wired by the overlay in PF-D; NEVER invoked in
    /// PF-B (no dirty closable non-discard context exists). Its argument is the tab index to close.</summary>
    public Action<int, GameState>? ConfirmDirtyClose { get; set; }

    public EditorTransport(World world, EditorHistory history,
        EditorShellStateComponent? shellState = null, string? sceneId = null,
        ViewportContextStack? stack = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        var shell = shellState ?? new EditorShellStateComponent();
        // The ONE context-switching mechanism (PF-B). The sweep it runs on every switch is THIS transport's
        // survivor-sparing sweep, so tab switches and Restart share the exact same teardown boundary.
        if (stack != null)
        {
            // TB-A: a host-scoped EditorSession owns the stack (its tab list survives a screen switch).
            // (Re)bind it to THIS screen's history + shell, and point its sweep at this transport.
            _stack = stack;
            _stack.Rebind(history, shell);
            _stack.SweepSceneEntities = DisposeSceneEntities;
        }
        else
        {
            // No session (tests / a standalone single-screen transport): own a fresh stack.
            _stack = new ViewportContextStack(history, shell, sceneId ?? EditorOverlay.DefaultSceneId)
            {
                SweepSceneEntities = DisposeSceneEntities,
            };
        }
    }

    /// <summary>The screen's "re-publish my original load request" callback — set it in the
    /// screen's <c>Load</c> once it knows what it loaded. Null = Restart is a loud no-op.</summary>
    public Action? Reload { get; set; }

    /// <summary>Optional screen exclusions from the restart sweep: return true for screen
    /// infrastructure that must survive (entities a system created once at construction and holds
    /// by reference — e.g. the dialogue UI root). Kept entities keep their <c>ChildOf</c>
    /// descendants too.</summary>
    public Func<Entity, bool>? KeepAlive { get; set; }

    // ─── The viewport context stack (PF-B) — the transport drives it, the seams forward to it ───────

    /// <summary>The viewport context stack this transport drives (the ONE tab-switching mechanism).
    /// Exposed so the overlay can wire the tab-strip system + read the active context, and for tests.</summary>
    public ViewportContextStack ContextStack => _stack;

    /// <summary>The active viewport tab's kind — the ONE mode signal (supersedes the retired
    /// <c>ViewMode</c>). <see cref="ViewportContextKind.Scene"/> by default; <see cref="ViewportContextKind.Game"/>
    /// while the Game sandbox tab is active.</summary>
    public ViewportContextKind ActiveContextKind => _stack.ActiveKind;

    /// <summary>The dirty state captured on the Scene context when the Game tab was spawned — what the
    /// Scenes-panel dirty <c>●</c> / the status bar reflect while the Game tab is active (the SNAPSHOT's
    /// dirtiness, not sandbox churn). Meaningful only while <see cref="ActiveContextKind"/> is
    /// <see cref="ViewportContextKind.Game"/>.</summary>
    public bool SnapshotWasDirty => _stack.SnapshotWasDirty;

    /// <summary>Builds the in-memory scene snapshot (<c>SceneWriter.BuildScene(world, rig.AsCamera(),
    /// layers)</c> — a <see cref="SceneData"/>, no file I/O). Forwards to the stack; null disables the
    /// Game tab. Wired by the overlay after construction (like <see cref="Reload"/>).</summary>
    public Func<SceneData>? CaptureSnapshot { get => _stack.CaptureSnapshot; set => _stack.CaptureSnapshot = value; }

    /// <summary>Restores a snapshot THROUGH THE READER (an in-memory <c>LoadSceneRequest</c>) — the
    /// shared re-tag / rehydration / <c>DrawComponent</c> / rig-sync path (pre-mortem #2). Forwards to
    /// the stack.</summary>
    public Action<SceneData>? RestoreSnapshot { get => _stack.RestoreSnapshot; set => _stack.RestoreSnapshot = value; }

    /// <summary>Captures the free editor VIEW (the live <c>Camera</c>) so a switch can restore where the
    /// designer was looking. Forwards to the stack.</summary>
    public Func<CameraViewSnapshot>? CaptureView { get => _stack.CaptureView; set => _stack.CaptureView = value; }

    /// <summary>Restores a captured VIEW onto the live <c>Camera</c> (applied AFTER the reader's
    /// auto-frame, so the captured view wins — only when valid). Forwards to the stack.</summary>
    public Action<CameraViewSnapshot>? RestoreView { get => _stack.RestoreView; set => _stack.RestoreView = value; }

    /// <summary>Snaps the free VIEW onto the camera rig (<c>Camera := rig state</c>) — the game-camera
    /// view adopted on Game-tab entry. Forwards to the stack (the overlay wires
    /// <c>EditorCameraRig.SnapViewToRig</c>).</summary>
    public Action? SnapViewToRig { get => _stack.SnapViewToRig; set => _stack.SnapViewToRig = value; }

    /// <summary>Updates the ACTIVE context's scene id + label (the overlay's <c>SetSceneId</c> in
    /// <c>Load</c>), so the status bar / active tab track the scene the live screen loaded (TB-A — the
    /// active tab, not necessarily the boot tab, since several named scene tabs may be open).</summary>
    public void SetSceneId(string sceneId) => _stack.SetActiveSceneId(sceneId ?? EditorOverlay.DefaultSceneId);

    /// <summary>Sets the active scene tab's owning screen name when it does not yet carry one — the boot
    /// tab learns its screen when the first overlay binds the Scenes catalog (TB-A).</summary>
    public void SetScreenName(string? screenName) => _stack.SetActiveScreenName(screenName);

    // ─── Transport (RunMode) ────────────────────────────────────────────────────────────────────────

    /// <summary>Resume the game (Playing = <see cref="RunMode.Play"/>). No-op when already playing.
    /// <para><b>Play from the Scene tab spawns the Game tab (PF-B, pre-mortem #7).</b> Pressing Play while
    /// the Scene tab is active first <see cref="EnterGameMode"/> — spawning + activating the Game tab and
    /// taking the snapshot <b>before</b> <see cref="GameState.RunMode"/> flips to Play, so no simulation
    /// frame can mutate the scene before it is captured. Pressing Play while the Game tab is already active
    /// just resumes (no re-snapshot — ONE snapshot per Game-tab session).</para></summary>
    public void Play(GameState state)
    {
        if (state.RunMode == RunMode.Play) return;
        if (_stack.ActiveKind == ViewportContextKind.Scene) EnterGameMode(state); // spawn the Game tab BEFORE the flip
        state.RunMode = RunMode.Play;
        Logger.Info("[level-editor] Transport: Playing.");
    }

    /// <summary>Freeze the game and hand the scene to the editing tools
    /// (Paused = <see cref="RunMode.Edit"/>). No-op when already paused.</summary>
    public void Pause(GameState state)
    {
        if (state.RunMode == RunMode.Edit) return;
        state.RunMode = RunMode.Edit;
        Logger.Info("[level-editor] Transport: Paused.");
    }

    /// <summary>The Play/Pause toggle button's action.</summary>
    public void TogglePlayPause(GameState state)
    {
        if (state.RunMode == RunMode.Play) Pause(state);
        else Play(state);
    }

    // ─── Scene / Game tab transitions (PF-B — drive the stack) ──────────────────────────────────────

    /// <summary>Toggles between the Scene tab and the Game tab: from Scene, <see cref="EnterGameMode"/>
    /// (spawn + activate the Game tab); from Game, <see cref="ExitToSceneMode"/> (discard + restore Scene).</summary>
    public void ToggleViewMode(GameState state)
    {
        if (_stack.ActiveKind == ViewportContextKind.Scene) EnterGameMode(state);
        else ExitToSceneMode(state);
    }

    /// <summary>
    /// Spawns + activates the Game-mode sandbox tab (the ex-Game-mode entry, PF-B): drives
    /// <see cref="ViewportContextStack.EnterGame"/>, which snapshots the Scene context FIRST (the restore
    /// point) — <b>before</b> anything can flip <see cref="GameState.RunMode"/> to Play (pre-mortem #7:
    /// <see cref="Play"/> calls this before <c>state.RunMode = Play</c>) — then adopts the game-camera view.
    /// No-op when the Game tab is already active (ONE snapshot per session). Does NOT itself change
    /// <see cref="GameState.RunMode"/> — <see cref="Play"/> flips it after this returns.
    /// </summary>
    public void EnterGameMode(GameState state)
    {
        if (_stack.ActiveKind == ViewportContextKind.Game) return;
        _stack.EnterGame();
        Logger.Info("[level-editor] Transport: spawned the Game tab (sandbox) — scene snapshotted; " +
                    "edits discard on leave, Save blocked.");
    }

    /// <summary>
    /// Leaves the Game-mode sandbox tab back to the Scene tab (PF-B): lands <b>Paused</b>
    /// (<see cref="RunMode.Edit"/>), then drives <see cref="ViewportContextStack.ExitToScene"/> — which
    /// disposes the sandbox scene entities (the SAME survivor-sparing sweep Restart uses), restores the
    /// Scene snapshot <b>through the reader</b> (shared re-tag / rehydration / <c>DrawComponent</c> /
    /// rig-sync path), clears the undo history (undo after leave is a no-op — pre-mortem #3), restores the
    /// captured dirty state + Scene VIEW (only when valid), and drops the Game tab from the strip (it
    /// never persists in the background). Sandbox edits vanish: <b>Scene shows exactly what Save would
    /// write.</b> No-op when already on the Scene tab.
    /// </summary>
    public void ExitToSceneMode(GameState state)
    {
        if (_stack.ActiveKind == ViewportContextKind.Scene) return;
        state.RunMode = RunMode.Edit;   // land Paused before restoring
        _stack.ExitToScene();
        Logger.Info("[level-editor] Transport: left the Game tab — sandbox discarded, scene restored " +
                    "from the snapshot. Paused.");
    }

    /// <summary>
    /// Opens (or re-activates) a prefab-context tab (PF-D): lands Paused (a prefab tab never plays — Play
    /// is disabled in a prefab context, v1) and drives <see cref="ViewportContextStack.OpenPrefab"/>, which
    /// snapshots the current context, sweeps, pushes a closable <see cref="ViewportContextKind.Prefab"/>
    /// tab, and reader-restores the prefab's content from <paramref name="prefabScene"/> with the camera
    /// rig suppressed (pre-mortem #8). The tab's label is the prefab id.
    /// </summary>
    public void OpenPrefab(string prefabId, SceneData prefabScene, GameState state)
    {
        state.RunMode = RunMode.Edit; // a prefab tab is always edited Paused (Play is disabled there)
        _stack.OpenPrefab(prefabId, prefabId, prefabScene);
        Logger.Info($"[level-editor] Transport: opened prefab tab '{prefabId}'. Paused.");
    }

    /// <summary>
    /// Switches the active viewport tab to <paramref name="index"/> (a Scenes-panel-style tab click, PF-B).
    /// Leaving the Game tab lands Paused and discards the sandbox (via the stack). Switching to a
    /// persistent context lands Paused. For PF-B the only reachable switch is Game → Scene (there is no
    /// background persistent tab to switch to yet). No-op on the active tab.
    /// </summary>
    public void SwitchToTab(int index, GameState state)
    {
        if (index == _stack.ActiveIndex) return;
        state.RunMode = RunMode.Edit;   // any tab switch lands Paused
        _stack.SwitchTo(index);
        Logger.Info($"[level-editor] Transport: switched to tab #{index} ({_stack.ActiveKind}). Paused.");
    }

    /// <summary>
    /// Closes the viewport tab at <paramref name="index"/> (its <c>×</c> / the <c>tab:close</c> op) through
    /// the stack's dirty-close gate (pre-mortem #9): the Scene tab is refused (never silently discarded);
    /// the Game tab discards immediately (its <c>×</c> is <see cref="ExitToSceneMode"/> — no dialog); a
    /// dirty persistent closable tab (a future prefab tab) routes the <see cref="ConfirmDirtyClose"/>
    /// confirm; a clean one closes and returns to the Scene tab.
    /// </summary>
    public void CloseTab(int index, GameState state)
    {
        switch (_stack.DecideClose(index))
        {
            case ViewportCloseDecision.Refused:
                Logger.Warning($"[level-editor] Transport: tab #{index} cannot be closed " +
                               "(the Scene tab is never closable, or the index is invalid).");
                return;
            case ViewportCloseDecision.DiscardImmediately: // the Game tab — discard the sandbox, no dialog
                ExitToSceneMode(state);
                return;
            case ViewportCloseDecision.ConfirmDirty: // a dirty prefab tab (PF-D) — route the confirm flow
                // Make the tab active FIRST (if it isn't) so the confirm's Save/Discard operate on ITS
                // world, then route the Save & Close / Discard / Cancel confirm on the now-active index.
                if (index != _stack.ActiveIndex) SwitchToTab(index, state);
                if (ConfirmDirtyClose != null) ConfirmDirtyClose(_stack.ActiveIndex, state);
                else Logger.Warning($"[level-editor] Transport: tab #{index} is dirty but no confirm-close " +
                                     "flow is wired (PF-D wires ConfirmDirtyClose).");
                return;
            case ViewportCloseDecision.CloseClean:
                state.RunMode = RunMode.Edit;
                _stack.CloseCleanContext(index);
                return;
        }
    }

    /// <summary>
    /// Return the world to the state of the original load (see the class doc for the exact
    /// sequence and the survival boundary). Lands Paused, on the Scene tab. Unsaved edits are discarded.
    /// </summary>
    public void Restart(GameState state)
    {
        if (Reload == null)
        {
            Logger.Warning(
                "[level-editor] Transport: Restart requested but the screen registered no Reload " +
                "callback — nothing was torn down (a reloadless teardown would strand a blank world). " +
                "Set EditorTransport.Reload in the screen's Load.");
            return;
        }

        state.RunMode = RunMode.Edit; // Paused first, so nothing simulates over the teardown
        _history.Clear();             // undo entries reference the entities about to die

        // Drop any Game tab and forget the Scene context's in-memory snapshot: the snapshot IS an unsaved
        // edit, and Restart's contract is "discards unsaved edits" — the disk reload below is the source
        // of truth. Lands on the Scene tab.
        _stack.ResetToScene();

        // The world-level level components must go BEFORE the re-publish: the LDtk parsers react
        // to CurrentLevelComponent ADDED (a Set over a present component fires Changed instead).
        _world.Remove<CurrentLevelComponent>();
        _world.Remove<CurrentBackgroundColorComponent>();

        DisposeSceneEntities();
        Reload();

        Logger.Info("[level-editor] Transport: Restart — scene rebuilt from the original load " +
                    "request; unsaved edits (incl. any Game-tab sandbox) discarded. Scene tab, Paused.");
    }

    private void DisposeSceneEntities()
    {
        using var all = _world.GetEntities().AsSet();
        var entities = all.GetEntities().ToArray(); // snapshot: we dispose while walking
        foreach (var entity in entities)
        {
            if (!entity.IsAlive) continue; // already gone with a disposed parent
            if (Survives(entity)) continue;
            entity.Dispose();
        }
    }

    /// <summary>Whether <paramref name="entity"/> survives a restart / tab-switch sweep (see the class doc).</summary>
    public bool Survives(Entity entity) =>
        entity.Has<EditorInfrastructureComponent>()
        || entity.Has<CursorControllerComponent>()
        || entity.Has<CursorInputComponent>()
        || IsScreenInfrastructure(entity);

    /// <summary>Whether <paramref name="entity"/> is screen-held <b>KeepAlive infrastructure</b> — named
    /// by the screen's <see cref="KeepAlive"/> predicate, or a <c>ChildOf</c> descendant of one (keeps
    /// propagate down the chain). PF-F: the editor consults this to REFUSE deleting it (the crash fix — a
    /// screen system holds it live) and to HIDE it from the Entities tree in a prefab context (it is not
    /// prefab content). Distinct from <see cref="Survives"/>, which also spares editor chrome + the cursor.</summary>
    public bool IsScreenInfrastructure(Entity entity)
    {
        if (KeepAlive == null) return false;

        // Keeps propagate down the ChildOf chain: walk this entity's ancestors and keep the whole
        // branch when any of them is named by the screen.
        var current = entity;
        for (var depth = 0; depth < MaxParentWalk; depth++)
        {
            if (KeepAlive(current)) return true;
            if (!current.Has<ChildOfComponent>()) return false;
            var parent = current.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) return false;
            current = parent;
        }
        return false;
    }
}

/// <summary>A snapshot of the free editor VIEW (the live <c>Camera</c>) — position / zoom / rotation —
/// captured when a viewport context is backgrounded and restored on return so the designer comes back to
/// exactly where they were looking. Plain value data.
///
/// <para><b>Validity (UX3-A pre-mortem #2).</b> <see cref="Zoom"/> is a positive scale, so
/// <c>default(CameraViewSnapshot)</c> — the value an unwired <c>CaptureView</c> yields — has
/// <see cref="Zoom"/> <c>== 0</c> and is <b>not</b> <see cref="IsValid"/>. A restore must never apply
/// an invalid snapshot: <c>Camera.Zoom</c> clamps a zero to <c>0.1f</c>, so a naive restore of a zeroed
/// snapshot silently blanks the view (origin + a near-degenerate zoom). An invalid snapshot means "keep
/// the current view".</para></summary>
public readonly struct CameraViewSnapshot(Vector2 position, float zoom, float rotation)
{
    public readonly Vector2 Position = position;
    public readonly float Zoom = zoom;
    public readonly float Rotation = rotation;

    /// <summary>Whether this snapshot carries a usable view (a positive zoom). <c>default</c> — an
    /// unwired/zeroed <c>CaptureView</c> — is invalid, so a restore keeps the current view instead
    /// of blanking it (UX3-A pre-mortem #2).</summary>
    public bool IsValid => Zoom > 0f;
}
