#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's <b>viewport context stack</b> (PF-B / TB-A): the ONE mechanism that switches between the
/// open viewport tabs (pre-mortem #4 — there is exactly ONE snapshot/sweep/restore implementation; the
/// Game tab and the named scene tabs are its consumers, not parallel paths). A
/// <see cref="ViewportContext"/> = a tab: <c>{ Kind, Id, ScreenName, Label, a SceneData snapshot, a
/// CameraViewSnapshot view, the history dirty flag }</c> — <b>data only</b>, never a live World/Entity ref
/// (TB-A pre-mortem #1), so a context restored on a different screen instance rebuilds cleanly through the
/// reader.
///
/// <para><b>Host-scoped (TB-A).</b> The stack is owned by the host-scoped <see cref="EditorSession"/> (one
/// per host, beside the <c>ScreenController</c>) — its context list survives a screen switch, exactly as
/// <c>GameState</c> does. The per-screen <see cref="EditorHistory"/> + <see cref="EditorShellStateComponent"/>
/// and the world-facing seams are (re)bound on every overlay construction via <see cref="Rebind"/>: overlay
/// disposal detaches but never destroys the tab list. A bootstrap constructor (<see cref="ViewportContextStack(string,string)"/>)
/// seeds the boot scene tab before any screen exists; the standalone constructor binds immediately (tests).</para>
///
/// <para><b>Named per-scene tabs (TB-A).</b> The Scene tab is titled by its scene id; opening a scene the
/// panel offers <see cref="AddSceneContext"/>s a new tab (or activates the existing one). Switching tabs
/// NEVER discards — leaving a persistent context always <see cref="SnapshotActive"/>s it — so the old
/// "in-place switch dirty-gates" premise is superseded; only CLOSING gates
/// (<see cref="DecideClose"/>), and the LAST scene tab refuses to close.</para>
///
/// <para><b>The Game tab (the discard consumer).</b> <see cref="EnterGame"/> snapshots the active scene
/// context (the restore point), records its <see cref="GameOriginIndex"/>, adopts the game-camera view
/// (<see cref="SnapViewToRig"/>), and pushes a discard Game context <b>keeping the live world as the
/// sandbox</b> (NO sweep on enter). Leaving the Game tab is an ordinary <see cref="SwitchTo"/> back to its
/// origin scene context: because the Game context <see cref="ViewportContext.IsDiscard"/>, it is NEVER
/// re-snapshotted and is dropped from the strip afterward. Discard semantics survive VERBATIM.</para>
///
/// <para><b>Cross-screen (TB-A).</b> A scene tab may name a screen different from the live one. In-place
/// operations (<see cref="EnterGame"/>/<see cref="SwitchTo"/>/<see cref="CloseCleanContext"/>) are for
/// SAME-screen contexts; the <see cref="EditorOverlay"/> orchestrates cross-screen activation
/// (<see cref="SnapshotActive"/> + <see cref="SetActiveIndex"/> + a host <c>LoadScreen</c> hand-off + the
/// session's pending-activation restore on the new screen). The stack never does I/O and never reaches
/// across screens itself.</para>
/// </summary>
public sealed class ViewportContextStack
{
    private EditorHistory _history;
    private EditorShellStateComponent? _shell;
    private readonly List<ViewportContext> _contexts = new();
    private int _activeIndex;
    private int _gameOrigin;

    /// <summary>Standalone constructor (binds immediately): a shared edit history, the shell whose
    /// descriptors this rewrites, and the boot scene id. Used by tests and by a transport that owns its own
    /// stack (no host session).</summary>
    public ViewportContextStack(EditorHistory history, EditorShellStateComponent shell, string sceneId,
        string? screenName = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Seed(sceneId, screenName);
        SyncDescriptors();
    }

    /// <summary>Bootstrap constructor (TB-A): seeds the boot scene tab with no per-screen deps yet — the
    /// host-scoped <see cref="EditorSession"/> creates the stack before any screen exists, and the first
    /// overlay <see cref="Rebind"/>s the history + shell. Nothing mutates the stack before that first bind.</summary>
    public ViewportContextStack(string sceneId, string? screenName = null)
    {
        _history = null!;   // rebound before any mutation (SnapshotActive/DecideClose guard against null)
        _shell = null;      // rebound before any SyncDescriptors
        Seed(sceneId, screenName);
    }

    private void Seed(string sceneId, string? screenName)
    {
        var id = sceneId ?? EditorOverlay.DefaultSceneId;
        _contexts.Add(new ViewportContext(ViewportContextKind.Scene, id, id, closable: false, isDiscard: false)
        {
            ScreenName = screenName,
        });
        _activeIndex = 0;
        _gameOrigin = 0;
    }

    /// <summary>
    /// (Re)binds the stack's per-screen dependencies — the new screen's <see cref="EditorHistory"/> and
    /// <see cref="EditorShellStateComponent"/> — and re-syncs the tab-strip descriptors onto that shell
    /// (TB-A). The persistent context list + active index are untouched, so the new screen shows the same
    /// tabs. Called by the transport on every overlay construction.
    /// </summary>
    public void Rebind(EditorHistory history, EditorShellStateComponent shell)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        SyncDescriptors();
    }

    // ─── Seams (wired by the overlay, or the transport's forwarding facade) ─────────────────────────

    /// <summary>Builds the in-memory snapshot of the active context (<c>SceneWriter.BuildScene(world,
    /// rig.AsCamera(), layers)</c> — a <see cref="SceneData"/>, no file I/O). Null disables snapshotting.</summary>
    public Func<SceneData>? CaptureSnapshot { get; set; }

    /// <summary>Restores a snapshot THROUGH THE READER (an in-memory <c>LoadSceneRequest(SceneData)</c>),
    /// so re-tag / texture rehydration / <c>DrawComponent</c> restore / camera-rig re-sync are shared
    /// with the file load path (pre-mortem #2 — the reader is the ONE restore implementation).</summary>
    public Action<SceneData>? RestoreSnapshot { get; set; }

    /// <summary>Captures the free editor VIEW (the live <c>Camera</c> position/zoom/rotation).</summary>
    public Func<CameraViewSnapshot>? CaptureView { get; set; }

    /// <summary>Restores a captured VIEW onto the live <c>Camera</c> (applied AFTER the reader's
    /// auto-frame, so the captured view wins — but only when it is <see cref="CameraViewSnapshot.IsValid"/>).</summary>
    public Action<CameraViewSnapshot>? RestoreView { get; set; }

    /// <summary>Snaps the free VIEW onto the camera rig (<c>Camera := rig state</c>) — the game-camera
    /// view adopted on Game-tab entry.</summary>
    public Action? SnapViewToRig { get; set; }

    /// <summary>Disposes the scene entities (the transport's survivor-sparing sweep — editor
    /// infrastructure / cursor / <c>KeepAlive</c> survive). Injected by the transport.</summary>
    public Action? SweepSceneEntities { get; set; }

    /// <summary>
    /// Rebuilds the screen's <b>CODE-OWNED content</b> (menu UI builders, demo create-methods) — the
    /// entities a screen creates in code that are NEVER <c>SceneObjectComponent</c>-tagged and so are
    /// never captured in a snapshot (TD). Injected by the transport (forwarding the screen's
    /// <see cref="EditorTransport.RebuildCodeContent"/>). Invoked <b>between the sweep and the reader
    /// restore</b> on every SAME-screen <see cref="SwitchTo"/> / <see cref="CloseCleanContext"/> whose
    /// target shows the screen's own content (a Scene/Game context, NEVER a Prefab context), so a
    /// Game-tab exit / same-screen scene switch keeps the code-built UI (the menu's buttons, the demo's
    /// entities) instead of sweeping to a blank screen and restoring an empty snapshot. Null (tests, or a
    /// screen that builds no code content — e.g. the level Game screen, whose content is scene-owned) →
    /// a no-op, so the pre-TD snapshot-only restore is byte-identical.
    /// </summary>
    public Action? RebuildCodeContent { get; set; }

    /// <summary>
    /// True for the exact duration of a <b>prefab-context</b> reader restore (PF-D, pre-mortem #8): the
    /// overlay's <see cref="RestoreSnapshot"/> reads it to publish the in-memory
    /// <c>LoadSceneRequest</c> with <c>SuppressCameraRig</c> set, so a prefab tab's content-load never
    /// syncs the camera rig (a prefab has none — a rig sync would corrupt the scene's authored camera).
    /// </summary>
    public bool RestoringPrefabContext { get; private set; }

    // ─── State ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The ordered contexts (tabs). The boot scene context is index 0.</summary>
    public IReadOnlyList<ViewportContext> Contexts => _contexts;

    /// <summary>The active context's index into <see cref="Contexts"/>.</summary>
    public int ActiveIndex => _activeIndex;

    /// <summary>The active context.</summary>
    public ViewportContext Active => _contexts[_activeIndex];

    /// <summary>The active context's kind — the ONE mode signal (supersedes the retired <c>ViewMode</c>).</summary>
    public ViewportContextKind ActiveKind => Active.Kind;

    /// <summary>The boot scene context (index 0). With named scene tabs it is one of possibly several
    /// scene tabs — the "Scene tab" that <see cref="EnterGame"/>/<see cref="SnapshotWasDirty"/> read is the
    /// Game tab's <see cref="GameOriginIndex"/> origin, not necessarily this one.</summary>
    public ViewportContext SceneContext => _contexts[0];

    /// <summary>The scene context a live Game tab was spawned from (its restore-on-exit target). 0 when no
    /// Game tab is active.</summary>
    public int GameOriginIndex => _gameOrigin;

    /// <summary>The dirty flag captured on the scene context a Game tab was spawned from — what the
    /// Scenes-panel <c>●</c> and the status bar reflect while the Game tab is active (the SNAPSHOT's
    /// dirtiness, not sandbox churn). Meaningful only while a Game context is active.</summary>
    public bool SnapshotWasDirty =>
        _gameOrigin >= 0 && _gameOrigin < _contexts.Count && _contexts[_gameOrigin].WasDirty;

    /// <summary>The number of open scene tabs — the last one refuses to close.</summary>
    public int SceneTabCount => _contexts.Count(c => c.Kind == ViewportContextKind.Scene);

    // ─── Naming (the overlay's SetSceneId / BindSceneCatalog in the screen's Load) ─────────────────────

    /// <summary>Sets the ACTIVE context's scene id — and, for a scene context, its display label too (a
    /// scene tab is titled by its id, TB-A). The screen calls this in <c>Load</c> from the level it loaded,
    /// so the active tab and Save target track the scene the live screen hosts. A blank id is ignored.</summary>
    public void SetActiveSceneId(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) return;
        var a = Active;
        a.Id = sceneId;
        if (a.Kind == ViewportContextKind.Scene) a.Label = sceneId;
        SyncDescriptors();
    }

    /// <summary>Sets the ACTIVE context's owning screen name (the screen the tab loads on), when it is a
    /// scene context that does not yet carry one — the boot tab learns its screen when the first overlay
    /// binds the Scenes catalog (TB-A).</summary>
    public void SetActiveScreenName(string? screenName)
    {
        var a = Active;
        if (a.Kind == ViewportContextKind.Scene && a.ScreenName == null) a.ScreenName = screenName;
    }

    /// <summary>The index of an OPEN scene tab with <paramref name="sceneId"/> (any screen), or -1.</summary>
    public int IndexOfSceneTab(string sceneId)
    {
        for (var i = 0; i < _contexts.Count; i++)
            if (_contexts[i].Kind == ViewportContextKind.Scene &&
                string.Equals(_contexts[i].Id, sceneId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>Appends a NEW scene tab (not activated) and returns its index — the Scenes-panel "open a
    /// scene not yet open" primitive (TB-A). The overlay then sets it active (same-screen in-place, or
    /// cross-screen via the host hand-off).</summary>
    public int AddSceneContext(string sceneId, string? screenName, string? label = null)
    {
        var id = sceneId ?? EditorOverlay.DefaultSceneId;
        _contexts.Add(new ViewportContext(ViewportContextKind.Scene, id, label ?? id,
            closable: true, isDiscard: false) { ScreenName = screenName });
        SyncDescriptors();
        return _contexts.Count - 1;
    }

    /// <summary>Sets the active index WITHOUT sweep/restore (the cross-screen path: the host screen switch
    /// rebuilds the world, and the new screen's overlay restores the target through the reader). Snapshots
    /// nothing itself — the caller <see cref="SnapshotActive"/>s the leaving context first.</summary>
    public void SetActiveIndex(int index)
    {
        if (index < 0 || index >= _contexts.Count) return;
        _activeIndex = index;
        SyncDescriptors();
    }

    /// <summary>
    /// Prepares a CROSS-SCREEN activation of the tab at <paramref name="index"/> (TB-A): a leaving Game tab
    /// is dropped (discard, no snapshot); a leaving persistent context is <see cref="SnapshotActive"/>d
    /// (preserved). Then the active index is set to the target. Does NOT sweep or restore — the host screen
    /// switch tears down + rebuilds the world, and the new screen's overlay restores the target snapshot
    /// through the reader. Returns the target's (possibly shifted) index.
    /// </summary>
    public int PrepareCrossScreenActivation(int index)
    {
        if (index < 0 || index >= _contexts.Count) return _activeIndex;
        var leaving = Active;
        var target = _contexts[index];
        if (leaving.IsDiscard)
        {
            _contexts.Remove(leaving); // the Game sandbox never persists in the background
        }
        else if (!ReferenceEquals(leaving, target))
        {
            SnapshotActive();          // preserve the leaving persistent context
        }
        _activeIndex = _contexts.IndexOf(target);
        SyncDescriptors();
        return _activeIndex;
    }

    // ─── Transitions ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enters the Game-mode sandbox on top of the current (scene) world (UX2-F, discard semantics verbatim).
    /// Records <see cref="GameOriginIndex"/>, snapshots the active scene context FIRST (before any RunMode
    /// flip, pre-mortem #7), adopts the game-camera view, and pushes a discard Game context KEEPING the live
    /// world as the sandbox. No-op when a Game context is already active (ONE snapshot per session).
    /// </summary>
    public void EnterGame()
    {
        if (ActiveKind == ViewportContextKind.Game) return; // one snapshot per Game-mode session
        _gameOrigin = _activeIndex;                          // the scene tab to restore on exit
        SnapshotActive();                                    // snapshot the origin scene — the restore point
        SnapViewToRig?.Invoke();                             // Camera := rig (the authored game-camera view)
        _contexts.Add(new ViewportContext(ViewportContextKind.Game, Active.Id, "Game",
            closable: true, isDiscard: true));
        _activeIndex = _contexts.Count - 1;
        SyncDescriptors();
    }

    /// <summary>
    /// Switches to the context at <paramref name="index"/> IN PLACE (a SAME-screen tab switch): snapshot the
    /// active context UNLESS it is a discard context (the Game tab is never re-snapshotted on leave) → sweep
    /// → reader-restore the target's snapshot → restore the target's view + history state. A discard context
    /// being left is dropped from the strip afterward. No-op when already at <paramref name="index"/>.
    /// </summary>
    public void SwitchTo(int index)
    {
        if (index < 0 || index >= _contexts.Count || index == _activeIndex) return;

        var leaving = Active;
        var target = _contexts[index];

        if (!leaving.IsDiscard) SnapshotActive();       // a persistent context is preserved (never discarded)
        SweepSceneEntities?.Invoke();                   // the transport's survivor-sparing sweep

        // TD: rebuild the screen's code-owned content (never snapshot-captured) BETWEEN the sweep and the
        // reader restore, so a Game-tab exit / same-screen scene switch keeps the menu UI / demo entities
        // instead of a blank screen. A Prefab target is isolated (only the prefab shows), so skip it.
        if (target.Kind != ViewportContextKind.Prefab) RebuildCodeContent?.Invoke();

        // A prefab target restores WITHOUT the camera rig (PF-D, pre-mortem #8 — it has none).
        RestoringPrefabContext = target.Kind == ViewportContextKind.Prefab;
        if (target.Snapshot != null) RestoreSnapshot?.Invoke(target.Snapshot); // through the reader (shared path)
        RestoringPrefabContext = false;
        _history?.Clear();                              // restored entities invalidate old commands (pre-mortem #3)
        if (target.WasDirty) _history?.MarkDirty();     // reproduce the target's captured dirtiness
        // Restore the captured VIEW over the reader's auto-frame — but only when valid (UX3-A pre-mortem
        // #2): a zeroed/unwired capture (Zoom == 0) would let Camera.Zoom clamp to 0.1f and blank the view.
        if (target.View.IsValid) RestoreView?.Invoke(target.View);

        if (leaving.IsDiscard) _contexts.Remove(leaving); // the Game tab disappears — it never persists
        _activeIndex = _contexts.IndexOf(target);
        SyncDescriptors();
    }

    /// <summary>Switches back to the scene context the live Game tab was spawned from (its
    /// <see cref="GameOriginIndex"/>) — the ex-<c>ExitToSceneMode</c> restore. When no Game tab is active,
    /// falls back to index 0.</summary>
    public void ExitToScene() => SwitchTo(_gameOrigin >= 0 && _gameOrigin < _contexts.Count ? _gameOrigin : 0);

    /// <summary>
    /// TB-A cross-screen restore: reader-restore the ACTIVE context's snapshot WITHOUT sweeping (the new
    /// screen skipped its fresh content load, so there is no scene content to clear — the code-built UI on a
    /// bound screen is preserved), clear the history, reproduce the captured dirty, restore the view. The
    /// overlay's <c>RestorePendingActivation</c> calls this after a cross-screen host screen switch.
    /// </summary>
    public void RestoreActiveSnapshot()
    {
        var target = Active;
        RestoringPrefabContext = target.Kind == ViewportContextKind.Prefab;
        if (target.Snapshot != null) RestoreSnapshot?.Invoke(target.Snapshot);
        RestoringPrefabContext = false;
        _history?.Clear();
        if (target.WasDirty) _history?.MarkDirty();
        if (target.View.IsValid) RestoreView?.Invoke(target.View);
    }

    /// <summary>Drops the context at <paramref name="index"/> WITHOUT sweeping or restoring (a background
    /// close, or the cross-screen active-close path where the overlay hands off to a host screen switch),
    /// aiming the active index at <paramref name="newActive"/> (clamped). The persistent list shrinks; the
    /// tab strip re-syncs.</summary>
    public void RemoveContextAt(int index, int newActive)
    {
        if (index < 0 || index >= _contexts.Count) return;
        _contexts.RemoveAt(index);
        _activeIndex = _contexts.Count == 0 ? 0 : Math.Clamp(newActive, 0, _contexts.Count - 1);
        SyncDescriptors();
    }

    /// <summary>
    /// Opens (or re-activates) a <b>prefab context</b> tab (PF-D): an empty world loaded with the prefab's
    /// entities from <paramref name="prefabScene"/>, auto-framed, its own scene-id and dirty/save-point.
    /// One tab per prefab.
    /// </summary>
    public void OpenPrefab(string prefabId, string label, SceneData prefabScene)
    {
        // Already open → activate it (one tab per prefab).
        var existing = IndexOfId(prefabId);
        if (existing >= 0 && _contexts[existing].Kind == ViewportContextKind.Prefab)
        {
            SwitchTo(existing);
            return;
        }

        var leaving = Active;
        if (!leaving.IsDiscard) SnapshotActive();       // preserve the current (Scene/prefab) context
        SweepSceneEntities?.Invoke();

        var ctx = new ViewportContext(ViewportContextKind.Prefab, prefabId ?? "prefab", label ?? prefabId ?? "prefab",
            closable: true, isDiscard: false);
        _contexts.Add(ctx);

        // Load the prefab's entities through the reader with the rig suppressed + view auto-framed.
        RestoringPrefabContext = true;
        RestoreSnapshot?.Invoke(prefabScene);
        RestoringPrefabContext = false;

        _history?.Clear();                              // a fresh prefab context is clean (its own save-point)

        if (leaving.IsDiscard) _contexts.Remove(leaving); // a Game sandbox never persists in the background
        _activeIndex = _contexts.IndexOf(ctx);
        SyncDescriptors();
    }

    /// <summary>
    /// Resets to a single, freshly-loaded scene context — the transport's Restart hook. Drops every
    /// non-Scene context (incl. any Game sandbox) AND every scene tab except the ACTIVE one (a Restart
    /// reloads the active scene from disk; the other tabs' in-memory snapshots are unsaved edits its discard
    /// contract covers), and clears the survivor's stored snapshot / dirty / view. Active becomes that lone
    /// scene context. Does NOT itself sweep or reload — the transport does both around this call.
    /// </summary>
    public void ResetToScene()
    {
        var keep = Active.Kind == ViewportContextKind.Scene ? Active : _contexts[0];
        _contexts.RemoveAll(c => !ReferenceEquals(c, keep));
        keep.Snapshot = null;
        keep.WasDirty = false;
        keep.View = default;
        _activeIndex = 0;
        _gameOrigin = 0;
        SyncDescriptors();
    }

    // ─── Close (the × affordance / tab:close) ───────────────────────────────────────────────────────

    /// <summary>
    /// The pure close decision for the tab at <paramref name="index"/> (the dirty-close gate, TB-A):
    /// <list type="bullet">
    ///   <item><see cref="ViewportCloseDecision.Refused"/> — a bad index, or the LAST scene tab (a scene
    ///   context is only refused when it is the sole one — a dirty scene is never silently discarded by a
    ///   stack op).</item>
    ///   <item><see cref="ViewportCloseDecision.DiscardImmediately"/> — a discard context (the Game tab).</item>
    ///   <item><see cref="ViewportCloseDecision.ConfirmDirty"/> — a dirty scene/prefab tab.</item>
    ///   <item><see cref="ViewportCloseDecision.CloseClean"/> — a clean scene/prefab tab.</item>
    /// </list>
    /// </summary>
    public ViewportCloseDecision DecideClose(int index)
    {
        if (index < 0 || index >= _contexts.Count) return ViewportCloseDecision.Refused;
        var ctx = _contexts[index];
        if (ctx.IsDiscard) return ViewportCloseDecision.DiscardImmediately;        // Game — discard, no dialog
        if (ctx.Kind == ViewportContextKind.Scene && SceneTabCount <= 1)
            return ViewportCloseDecision.Refused;                                  // the last scene tab
        return IsContextDirty(index) ? ViewportCloseDecision.ConfirmDirty : ViewportCloseDecision.CloseClean;
    }

    /// <summary>Whether the context at <paramref name="index"/> has unsaved edits — the live history for
    /// the ACTIVE context, else the context's captured <see cref="ViewportContext.WasDirty"/>.</summary>
    public bool IsContextDirty(int index)
    {
        if (index < 0 || index >= _contexts.Count) return false;
        return index == _activeIndex ? (_history?.IsDirty ?? false) : _contexts[index].WasDirty;
    }

    /// <summary>Finds the index of the (first) context whose <see cref="ViewportContext.Id"/> matches
    /// <paramref name="id"/>, or -1. Falls back to the KIND name ("scene" / "game") for the built-in tabs.</summary>
    public int IndexOfId(string id)
    {
        for (var i = 0; i < _contexts.Count; i++)
        {
            if (string.Equals(_contexts[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            if (string.Equals(_contexts[i].Kind.ToString(), id, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Closes a closable non-discard context (a scene tab that is not the last, or a prefab tab — TB-A/PF-D):
    /// drops it WITHOUT snapshotting it. Closing a BACKGROUND tab just removes it (its held snapshot is
    /// discarded) and keeps the active pointer aimed. Closing the ACTIVE tab sweeps and reader-restores an
    /// ADJACENT persistent tab (the previous one, clamped) — SAME-screen only; the overlay handles a
    /// cross-screen neighbour. Discard contexts (the Game tab) close via <see cref="ExitToScene"/> instead.
    /// </summary>
    public void CloseCleanContext(int index)
    {
        if (index < 0 || index >= _contexts.Count) return;
        var ctx = _contexts[index];
        if (ctx.IsDiscard) return;                       // Game (discard via ExitToScene)
        if (ctx.Kind == ViewportContextKind.Scene && SceneTabCount <= 1) return; // never close the last scene tab

        var closingActive = index == _activeIndex;
        _contexts.RemoveAt(index);

        if (closingActive)
        {
            var neighbour = Math.Clamp(index - 1, 0, _contexts.Count - 1);
            var target = _contexts[neighbour];
            SweepSceneEntities?.Invoke();
            _history?.Clear();
            // TD: rebuild code-owned content between the sweep and the restore (as SwitchTo does), so
            // closing the active tab back to a Scene/Game neighbour keeps the screen's code-built UI.
            if (target.Kind != ViewportContextKind.Prefab) RebuildCodeContent?.Invoke();
            RestoringPrefabContext = target.Kind == ViewportContextKind.Prefab;
            if (target.Snapshot != null) RestoreSnapshot?.Invoke(target.Snapshot);
            RestoringPrefabContext = false;
            if (target.WasDirty) _history?.MarkDirty();
            if (target.View.IsValid) RestoreView?.Invoke(target.View);
            _activeIndex = neighbour;
        }
        else if (index < _activeIndex)
        {
            _activeIndex--; // a lower-indexed background tab closed → keep the active pointer aimed
        }

        SyncDescriptors();
    }

    // ─── Internals ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Captures the active context's restore state — its <see cref="SceneData"/>, the history
    /// dirty flag, and the free VIEW — before the world is swept. Public so the overlay's cross-screen
    /// orchestration can preserve the leaving context before a host screen switch.</summary>
    public void SnapshotActive()
    {
        var a = Active;
        a.Snapshot = CaptureSnapshot?.Invoke();
        a.WasDirty = _history?.IsDirty ?? false;
        a.View = CaptureView?.Invoke() ?? default;
    }

    /// <summary>Rewrites the shell-state descriptor list + active index from the current contexts — the
    /// ONE writer of the tab strip's render source. A scene tab is closable iff more than one is open (the
    /// last one refuses to close), and its descriptor label is its scene id (TB-A). No-op until a shell is
    /// bound (the bootstrap state before the first overlay).</summary>
    private void SyncDescriptors()
    {
        if (_shell == null) return;
        var sceneTabs = SceneTabCount;
        _shell.ViewportTabs = _contexts
            .Select(c => new ViewportTabDescriptor(
                c.Kind, c.Id, c.Label,
                Closable: c.Kind switch
                {
                    ViewportContextKind.Scene => sceneTabs > 1, // the last scene tab is not closable
                    _ => c.Closable,
                }))
            .ToArray();
        _shell.ActiveViewportTab = _activeIndex;
    }
}

/// <summary>
/// One viewport context (a tab): its descriptor (<see cref="Kind"/> / <see cref="Id"/> /
/// <see cref="ScreenName"/> / <see cref="Label"/> / <see cref="Closable"/>) plus the in-memory restore
/// state captured when it is backgrounded — the <see cref="Snapshot"/> (<see cref="SceneData"/>), the free
/// <see cref="View"/>, and the <see cref="WasDirty"/> history flag. <b>Data only</b> — never a live
/// World/Entity ref (TB-A pre-mortem #1), so a context restored on a different screen instance rebuilds
/// cleanly through the reader. A mutable holder (the stack updates its snapshot in place).
///
/// <para>The context does NOT carry a <c>RunMode</c>: the <see cref="EditorTransport"/> is the ONE owner
/// of the live <see cref="MonoDreams.State.RunMode"/>. Leaving the Game tab always lands Paused; the
/// Scene/Prefab tabs are edited Paused.</para>
/// </summary>
public sealed class ViewportContext
{
    public ViewportContext(ViewportContextKind kind, string id, string label, bool closable, bool isDiscard)
    {
        Kind = kind;
        Id = id;
        Label = label;
        Closable = closable;
        IsDiscard = isDiscard;
    }

    /// <summary>The context kind (Scene / Game / Prefab).</summary>
    public ViewportContextKind Kind { get; }

    /// <summary>The context id (scene / prefab id).</summary>
    public string Id { get; set; }

    /// <summary>The screen this scene tab loads on (TB-A) — used to decide same-screen in-place activation
    /// vs a cross-screen host hand-off. Null for the boot tab until the first overlay binds the catalog,
    /// and for the Game/Prefab tabs (which never drive a cross-screen switch of their own).</summary>
    public string? ScreenName { get; set; }

    /// <summary>The tab's display label — the scene id for a scene tab (TB-A), "Game" for the Game tab, a
    /// prefab name for a prefab tab.</summary>
    public string Label { get; set; }

    /// <summary>Whether the tab is inherently closable (Scene tabs additionally refuse when they are the
    /// last one — see <see cref="ViewportContextStack.DecideClose"/>).</summary>
    public bool Closable { get; }

    /// <summary>Whether leaving this context DISCARDS it (the Game sandbox): never re-snapshotted on
    /// leave, dropped from the strip afterward, and its <c>×</c> is discard-by-nature (no confirm dialog).</summary>
    public bool IsDiscard { get; }

    /// <summary>The in-memory scene snapshot captured when this context was backgrounded (the restore
    /// point). Null before the first snapshot / after a Restart reset.</summary>
    public SceneData? Snapshot { get; set; }

    /// <summary>The free editor VIEW captured when this context was backgrounded (restored on return,
    /// only when <see cref="CameraViewSnapshot.IsValid"/>).</summary>
    public CameraViewSnapshot View { get; set; }

    /// <summary>The history dirty flag captured when this context was backgrounded — reproduced on
    /// return (via <c>EditorHistory.MarkDirty</c>) so the restored dirtiness matches the pre-leave state.</summary>
    public bool WasDirty { get; set; }
}

/// <summary>The pure close decision for a viewport tab (the dirty-close gate) —
/// see <see cref="ViewportContextStack.DecideClose"/>.</summary>
public enum ViewportCloseDecision
{
    /// <summary>The tab cannot be closed (a bad index, or the last scene tab) — a dirty scene is never
    /// silently discarded.</summary>
    Refused,

    /// <summary>A discard context (the Game tab): close discards the sandbox with no dialog.</summary>
    DiscardImmediately,

    /// <summary>A dirty persistent closable context (a scene tab that is not the last, or a prefab tab):
    /// route the Save &amp; Close / Discard / Cancel confirm.</summary>
    ConfirmDirty,

    /// <summary>A clean persistent closable context: close without a prompt.</summary>
    CloseClean,
}
