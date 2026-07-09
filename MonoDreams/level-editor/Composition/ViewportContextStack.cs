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
/// The editor's <b>viewport context stack</b> (PF-B): the ONE mechanism that switches between the open
/// viewport tabs (pre-mortem #4 — there is exactly ONE snapshot/sweep/restore implementation; the Game
/// tab is its FIRST consumer, not a parallel path). A <see cref="ViewportContext"/> = a tab:
/// <c>{ Kind, Id, Label, a SceneData snapshot, a CameraViewSnapshot view, the history dirty flag }</c>.
///
/// <para><b>The mechanism.</b> Switching to an existing context = <see cref="SnapshotActive"/> the
/// context being left (its <c>SceneData</c> via <see cref="CaptureSnapshot"/> + the free VIEW via
/// <see cref="CaptureView"/> + the history dirty flag) → <see cref="SweepSceneEntities"/> (the
/// transport's survivor-sparing sweep) → reader-restore the target's snapshot (the in-memory
/// <c>LoadSceneRequest(SceneData)</c> path via <see cref="RestoreSnapshot"/> — so re-tag / texture
/// rehydration / <c>DrawComponent</c> restore / camera-rig re-sync are ALL shared with the file load) →
/// restore the target's view + history state. This is the generalized UX2-F Game-mode machinery.</para>
///
/// <para><b>The Game tab (the discard consumer).</b> <see cref="EnterGame"/> is the ex-<c>EnterGameMode</c>:
/// it snapshots the ACTIVE (Scene) context — the restore point — adopts the game-camera view
/// (<see cref="SnapViewToRig"/>), and pushes a discard Game context <b>keeping the live world as the
/// sandbox</b> (NO sweep on enter — the world already IS the scene). Leaving the Game tab is an ordinary
/// <see cref="SwitchTo"/> back to the Scene context: because the Game context <see cref="ViewportContext.IsDiscard"/>,
/// it is NEVER re-snapshotted on leave (its edits vanish) and is dropped from the strip afterward — it
/// never persists a background context. This preserves the UX2-F discard semantics verbatim.</para>
///
/// <para><b>Data safety (pre-mortem #9).</b> The Scene context is never <see cref="ViewportContext.Closable"/>
/// and is never a discard context, so <see cref="SwitchTo"/> ALWAYS snapshots it when leaving and
/// <see cref="DecideClose"/> refuses to close it — <b>a dirty Scene context can never be silently
/// discarded by any stack operation</b> (only the transport's Restart discards it, and that is the
/// explicit "discard unsaved edits, reload from disk" contract). The dirty-close gate
/// (<see cref="DecideClose"/> → <see cref="ViewportCloseDecision.ConfirmDirty"/>) is built now for the
/// future prefab tab; the Game tab's <c>×</c> is discard-by-nature (<see cref="ViewportCloseDecision.DiscardImmediately"/>,
/// no dialog).</para>
///
/// <para><b>Descriptor-driven strip.</b> The stack is the ONE writer of
/// <see cref="EditorShellStateComponent.ViewportTabs"/> — it rewrites the descriptor list on every
/// mutation, so the tab-strip renderer reads pure data and PF-D can append a prefab tab without touching
/// the renderer. Composition infrastructure the <see cref="EditorTransport"/> owns and drives (like
/// <see cref="Undo.EditorHistory"/>), not a per-frame system.</para>
/// </summary>
public sealed class ViewportContextStack
{
    private readonly EditorHistory _history;
    private readonly EditorShellStateComponent _shell;
    private readonly List<ViewportContext> _contexts = new();
    private int _activeIndex;

    /// <param name="history">The shared edit history — the dirty source captured on snapshot and cleared
    /// on restore (the SAME history the transport clears on Restart).</param>
    /// <param name="shell">The shell-state component whose <see cref="EditorShellStateComponent.ViewportTabs"/>
    /// this stack rewrites (the tab strip's render source).</param>
    /// <param name="sceneId">The Scene context's id (the scene being edited).</param>
    public ViewportContextStack(EditorHistory history, EditorShellStateComponent shell, string sceneId)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _contexts.Add(new ViewportContext(ViewportContextKind.Scene, sceneId ?? "untitled", "Scene",
            closable: false, isDiscard: false));
        _activeIndex = 0;
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
    /// True for the exact duration of a <b>prefab-context</b> reader restore (PF-D, pre-mortem #8): the
    /// overlay's <see cref="RestoreSnapshot"/> reads it to publish the in-memory
    /// <c>LoadSceneRequest</c> with <c>SuppressCameraRig</c> set, so a prefab tab's content-load never
    /// syncs the camera rig (a prefab has none — a rig sync would corrupt the scene's authored camera).
    /// Set immediately before, and cleared immediately after, each synchronous restore of a
    /// <see cref="ViewportContextKind.Prefab"/> context (open + tab-switch); false for a Scene / Game
    /// restore. Because the restore publish is synchronous (the reader runs inline), the flag is valid
    /// exactly while the reader reads it.
    /// </summary>
    public bool RestoringPrefabContext { get; private set; }

    // ─── State ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The ordered contexts (tabs). The Scene context is always index 0.</summary>
    public IReadOnlyList<ViewportContext> Contexts => _contexts;

    /// <summary>The active context's index into <see cref="Contexts"/>.</summary>
    public int ActiveIndex => _activeIndex;

    /// <summary>The active context.</summary>
    public ViewportContext Active => _contexts[_activeIndex];

    /// <summary>The active context's kind — the ONE mode signal (supersedes the retired <c>ViewMode</c>).</summary>
    public ViewportContextKind ActiveKind => Active.Kind;

    /// <summary>The always-present, never-closable Scene context (index 0).</summary>
    public ViewportContext SceneContext => _contexts[0];

    /// <summary>The dirty flag captured on the Scene context when a Game tab was spawned — what the
    /// Scenes-panel <c>●</c> and the status bar reflect while the Game tab is active (the SNAPSHOT's
    /// dirtiness, not sandbox churn). Meaningful only while a Game context is active.</summary>
    public bool SnapshotWasDirty => SceneContext.WasDirty;

    // ─── Transitions ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enters the Game-mode sandbox on top of the current (Scene) world — the ex-<c>EnterGameMode</c>
    /// (UX2-F). Snapshots the active (Scene) context FIRST (the restore point — before any RunMode flip,
    /// pre-mortem #7: the transport calls this before <c>RunMode = Play</c>), adopts the game-camera view
    /// (<see cref="SnapViewToRig"/>), and pushes a discard Game context KEEPING the live world as the
    /// sandbox (no sweep — the world already IS the scene). No-op when a Game context is already active
    /// (ONE snapshot per session). Requires the active context to be the Scene context.
    /// </summary>
    public void EnterGame()
    {
        if (ActiveKind == ViewportContextKind.Game) return; // one snapshot per Game-mode session
        SnapshotActive();                                   // snapshot the Scene — the restore point
        SnapViewToRig?.Invoke();                            // Camera := rig (the authored game-camera view)
        _contexts.Add(new ViewportContext(ViewportContextKind.Game, Active.Id, "Game",
            closable: true, isDiscard: true));
        _activeIndex = _contexts.Count - 1;
        SyncDescriptors();
    }

    /// <summary>
    /// Switches to the context at <paramref name="index"/> (a general tab switch): snapshot the active
    /// context UNLESS it is a discard context (the Game tab is never re-snapshotted on leave) → sweep →
    /// reader-restore the target's snapshot → restore the target's view + history state. A discard
    /// context being left is dropped from the strip afterward (it never persists in the background).
    /// No-op when already at <paramref name="index"/>.
    /// </summary>
    public void SwitchTo(int index)
    {
        if (index < 0 || index >= _contexts.Count || index == _activeIndex) return;

        var leaving = Active;
        var target = _contexts[index];

        if (!leaving.IsDiscard) SnapshotActive();       // a persistent context is preserved (never discarded)
        SweepSceneEntities?.Invoke();                   // the transport's survivor-sparing sweep

        // A prefab target restores WITHOUT the camera rig (PF-D, pre-mortem #8 — it has none).
        RestoringPrefabContext = target.Kind == ViewportContextKind.Prefab;
        if (target.Snapshot != null) RestoreSnapshot?.Invoke(target.Snapshot); // through the reader (shared path)
        RestoringPrefabContext = false;
        _history.Clear();                               // restored entities invalidate old commands (pre-mortem #3)
        if (target.WasDirty) _history.MarkDirty();      // reproduce the target's captured dirtiness
        // Restore the captured VIEW over the reader's auto-frame — but only when valid (UX3-A pre-mortem
        // #2): a zeroed/unwired capture (Zoom == 0) would let Camera.Zoom clamp to 0.1f and blank the view.
        if (target.View.IsValid) RestoreView?.Invoke(target.View);

        if (leaving.IsDiscard) _contexts.Remove(leaving); // the Game tab disappears — it never persists
        _activeIndex = _contexts.IndexOf(target);
        SyncDescriptors();
    }

    /// <summary>Switches back to the Scene context (index 0) — the ex-<c>ExitToSceneMode</c> restore.</summary>
    public void ExitToScene() => SwitchTo(0);

    /// <summary>
    /// Opens (or re-activates) a <b>prefab context</b> tab (PF-D): an empty world loaded with the prefab's
    /// entities from <paramref name="prefabScene"/> (the <c>.mdprefab</c>, source-first), auto-framed, its
    /// own scene-id (= <paramref name="prefabId"/>) and its own dirty/save-point. If a prefab tab for that
    /// id is already open, this just <see cref="SwitchTo"/>s it (one tab per prefab). Otherwise: snapshot
    /// the current context (a persistent one is preserved; a discard Game tab is dropped), sweep, push a
    /// closable non-discard <see cref="ViewportContextKind.Prefab"/> context, restore its content through
    /// the reader with the camera rig SUPPRESSED (pre-mortem #8 — a prefab has no rig), and clear the
    /// history so the fresh prefab context starts clean. Does NOT itself set <see cref="RunMode"/> — the
    /// transport lands it Paused (a prefab tab never plays).
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

        _history.Clear();                               // a fresh prefab context is clean (its own save-point)

        if (leaving.IsDiscard) _contexts.Remove(leaving); // a Game sandbox never persists in the background
        _activeIndex = _contexts.IndexOf(ctx);
        SyncDescriptors();
    }

    /// <summary>
    /// Resets to a single, freshly-loaded Scene context — the transport's Restart hook. Drops every
    /// non-Scene context (incl. any Game sandbox) and clears the Scene context's stored snapshot / dirty
    /// / view (a Restart reloads from disk, so the in-memory restore point is forgotten — the snapshot IS
    /// an unsaved edit Restart's discard contract covers). Active becomes the Scene context. Does NOT
    /// itself sweep or reload — the transport does both around this call.
    /// </summary>
    public void ResetToScene()
    {
        _contexts.RemoveAll(c => c.Kind != ViewportContextKind.Scene);
        var scene = SceneContext;
        scene.Snapshot = null;
        scene.WasDirty = false;
        scene.View = default;
        _activeIndex = 0;
        SyncDescriptors();
    }

    // ─── Close (the × affordance / tab:close) ───────────────────────────────────────────────────────

    /// <summary>
    /// The pure close decision for the tab at <paramref name="index"/> (the dirty-close gate,
    /// pre-mortem #9), routed by the transport's <c>CloseTab</c>:
    /// <list type="bullet">
    ///   <item><see cref="ViewportCloseDecision.Refused"/> — the Scene context (never closable) or a bad
    ///   index; a dirty Scene can never be silently discarded.</item>
    ///   <item><see cref="ViewportCloseDecision.DiscardImmediately"/> — a discard context (the Game tab):
    ///   its <c>×</c> discards the sandbox with no dialog (the existing exit semantics).</item>
    ///   <item><see cref="ViewportCloseDecision.ConfirmDirty"/> — a dirty persistent closable context
    ///   (the future prefab tab): route the Save &amp; Close / Discard / Cancel confirm.</item>
    ///   <item><see cref="ViewportCloseDecision.CloseClean"/> — a clean persistent closable context:
    ///   close it without a prompt.</item>
    /// </list>
    /// </summary>
    public ViewportCloseDecision DecideClose(int index)
    {
        if (index < 0 || index >= _contexts.Count) return ViewportCloseDecision.Refused;
        var ctx = _contexts[index];
        if (!ctx.Closable) return ViewportCloseDecision.Refused;      // Scene — never silently discarded
        if (ctx.IsDiscard) return ViewportCloseDecision.DiscardImmediately; // Game — discard, no dialog
        return IsContextDirty(index) ? ViewportCloseDecision.ConfirmDirty : ViewportCloseDecision.CloseClean;
    }

    /// <summary>Whether the context at <paramref name="index"/> has unsaved edits — the live history for
    /// the ACTIVE context, else the context's captured <see cref="ViewportContext.WasDirty"/>.</summary>
    public bool IsContextDirty(int index)
    {
        if (index < 0 || index >= _contexts.Count) return false;
        return index == _activeIndex ? _history.IsDirty : _contexts[index].WasDirty;
    }

    /// <summary>Finds the index of the (first) context whose <see cref="ViewportContext.Id"/> matches
    /// <paramref name="id"/>, or -1 — the <c>tab:close &lt;id&gt;</c> / <c>tab:&lt;id&gt;</c> lookup.
    /// Falls back to the KIND name ("scene" / "game") for the built-in tabs.</summary>
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
    /// Closes a closable non-discard context (a prefab tab — PF-D): drops it WITHOUT snapshotting it (a
    /// close discards the tab, whether it was clean or the caller already saved/discarded it via the
    /// dirty-close confirm). Closing the ACTIVE tab sweeps and restores the Scene context (returns to the
    /// Scene tab); closing a BACKGROUND tab just removes it (its held snapshot is discarded). Discard
    /// contexts (the Game tab) close via <see cref="ExitToScene"/> instead; the Scene context is never
    /// closable. This is the mechanism the clean-close path AND both dirty-close-confirm branches
    /// (Save &amp; Close / Discard &amp; Close) call.
    /// </summary>
    public void CloseCleanContext(int index)
    {
        if (index < 0 || index >= _contexts.Count) return;
        var ctx = _contexts[index];
        if (!ctx.Closable || ctx.IsDiscard) return; // Scene (never closable) / Game (discard via ExitToScene)

        var closingActive = index == _activeIndex;
        _contexts.RemoveAt(index);

        if (closingActive)
        {
            // Return to the Scene tab: sweep the closed context's world and reader-restore the Scene.
            SweepSceneEntities?.Invoke();
            var scene = SceneContext;
            _history.Clear();
            RestoringPrefabContext = false; // the Scene context always carries its rig
            if (scene.Snapshot != null) RestoreSnapshot?.Invoke(scene.Snapshot);
            if (scene.WasDirty) _history.MarkDirty();
            if (scene.View.IsValid) RestoreView?.Invoke(scene.View);
            _activeIndex = 0;
        }
        else if (index < _activeIndex)
        {
            _activeIndex--; // a lower-indexed background tab closed → keep the active pointer aimed
        }

        SyncDescriptors();
    }

    // ─── Internals ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Captures the active context's restore state — its <see cref="SceneData"/>, the history
    /// dirty flag, and the free VIEW — before the world is swept.</summary>
    private void SnapshotActive()
    {
        var a = Active;
        a.Snapshot = CaptureSnapshot?.Invoke();
        a.WasDirty = _history.IsDirty;
        a.View = CaptureView?.Invoke() ?? default;
    }

    /// <summary>Rewrites the shell-state descriptor list + active index from the current contexts — the
    /// ONE writer of the tab strip's render source.</summary>
    private void SyncDescriptors()
    {
        _shell.ViewportTabs = _contexts
            .Select(c => new ViewportTabDescriptor(c.Kind, c.Id, c.Label, c.Closable))
            .ToArray();
        _shell.ActiveViewportTab = _activeIndex;
    }
}

/// <summary>
/// One viewport context (a tab): its descriptor (<see cref="Kind"/> / <see cref="Id"/> / <see cref="Label"/> /
/// <see cref="Closable"/>) plus the in-memory restore state captured when it is backgrounded — the
/// <see cref="Snapshot"/> (<see cref="SceneData"/>), the free <see cref="View"/>, and the
/// <see cref="WasDirty"/> history flag. A mutable holder (the stack updates its snapshot in place).
///
/// <para>The context does NOT carry a <c>RunMode</c>: the <see cref="EditorTransport"/> is the ONE owner
/// of the live <see cref="MonoDreams.State.RunMode"/> (the "keep ONE owner" rule). Leaving the Game tab
/// always lands Paused; the Scene tab is always edited Paused. A future prefab tab plays nothing (v1).</para>
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

    /// <summary>The tab's display label ("Scene" / "Game" / a prefab name).</summary>
    public string Label { get; }

    /// <summary>Whether the tab shows a <c>×</c> close affordance (Scene = false).</summary>
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

/// <summary>The pure close decision for a viewport tab (the dirty-close gate, pre-mortem #9) —
/// see <see cref="ViewportContextStack.DecideClose"/>.</summary>
public enum ViewportCloseDecision
{
    /// <summary>The tab cannot be closed (the Scene context, or a bad index) — a dirty Scene is never
    /// silently discarded.</summary>
    Refused,

    /// <summary>A discard context (the Game tab): close discards the sandbox with no dialog.</summary>
    DiscardImmediately,

    /// <summary>A dirty persistent closable context (a future prefab tab): route the confirm flow.</summary>
    ConfirmDirty,

    /// <summary>A clean persistent closable context: close without a prompt.</summary>
    CloseClean,
}
