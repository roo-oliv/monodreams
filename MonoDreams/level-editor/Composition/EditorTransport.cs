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
/// The editor's <b>transport</b>: the one owner of <see cref="GameState.RunMode"/> — and, since
/// UX2-F, of the <see cref="ViewMode"/> (<see cref="EditorViewMode.Scene"/> /
/// <see cref="EditorViewMode.Game"/>) — under the editor run configuration. ONE owner for both.
/// With the editor composed, the shell and chrome are ALWAYS visible — no key
/// toggles the editor away — and the designer drives the game like a media player:
///
/// <list type="bullet">
///   <item><b>Paused</b> = <see cref="RunMode.Edit"/> — the game logic is Freeze-gated (holds
///   still), the editing tools (selection / gizmo / camera-nav / delete-undo-redo) are live.</item>
///   <item><b>Playing</b> = <see cref="RunMode.Play"/> — the game runs inside the inset viewport,
///   the shell stays composed (transport + systems panel remain interactive), and the editing
///   tools are inert: a click in the viewport belongs to the game while playing.</item>
///   <item><b>Restart</b> — return the world to the state of the ORIGINAL load: clear the undo
///   history (its entries reference entities about to die), remove the world-level level
///   components (<see cref="CurrentLevelComponent"/> / <see cref="CurrentBackgroundColorComponent"/> —
///   the LDtk parsers subscribe to the component <i>added</i> event, so a re-publish over a
///   still-set component would never re-parse), dispose every scene entity, re-run the screen's
///   recorded <see cref="Reload"/>, and land <b>Paused</b> (also when restarted mid-Play — the
///   predictable state to hand back to the designer). <b>Unsaved live edits are DISCARDED</b> —
///   the standard play-mode trade-off; Save first if you want to keep them.</item>
/// </list>
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
/// <see cref="Reload"/> re-creates it from the original load request.</para>
///
/// <para><b>The screen records what it loaded.</b> <see cref="Reload"/> is the screen-registered
/// "re-publish my original load request" callback (e.g.
/// <c>() =&gt; world.Publish(new LoadLevelRequest(levelId))</c>, or re-running the menu's UI
/// builder). Without it a Restart is a loud no-op — tearing the world down with no way to rebuild
/// it would strand the designer on a blank screen.</para>
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

    public EditorTransport(World world, EditorHistory history)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    /// <summary>The screen's "re-publish my original load request" callback — set it in the
    /// screen's <c>Load</c> once it knows what it loaded. Null = Restart is a loud no-op.</summary>
    public Action? Reload { get; set; }

    /// <summary>Optional screen exclusions from the restart sweep: return true for screen
    /// infrastructure that must survive (entities a system created once at construction and holds
    /// by reference — e.g. the dialogue UI root). Kept entities keep their <c>ChildOf</c>
    /// descendants too.</summary>
    public Func<Entity, bool>? KeepAlive { get; set; }

    // ─── Scene / Game view mode (UX2-F) — the transport is the ONE owner of both RunMode and ViewMode ───

    /// <summary>
    /// The editor's view mode (UX2-F), owned here alongside <see cref="GameState.RunMode"/> — ONE
    /// owner for both. <see cref="EditorViewMode.Scene"/> (default): the free editor view edits the
    /// REAL scene. <see cref="EditorViewMode.Game"/>: a Unity-style sandbox — the viewport looks
    /// through the game camera, edits are allowed while Paused "just to test", and are DISCARDED on
    /// exit (restored from the in-memory snapshot). Save is blocked in Game mode.
    /// </summary>
    public EditorViewMode ViewMode { get; private set; } = EditorViewMode.Scene;

    /// <summary>The dirty state captured when Game mode was entered — what the Scenes-panel dirty
    /// <c>●</c> reflects while in Game mode (the SNAPSHOT's dirtiness, not sandbox churn). Meaningful
    /// only while <see cref="ViewMode"/> is <see cref="EditorViewMode.Game"/>.</summary>
    public bool SnapshotWasDirty { get; private set; }

    /// <summary>Builds the in-memory Game-mode snapshot (<c>SceneWriter.BuildScene(world,
    /// rig.AsCamera(), layers)</c> — a <see cref="SceneData"/>, no file I/O). Wired by the overlay
    /// after construction (like <see cref="Reload"/>). Null disables Game mode.</summary>
    public Func<SceneData>? CaptureSnapshot { get; set; }

    /// <summary>Restores a snapshot THROUGH THE READER (the overlay publishes an in-memory
    /// <c>LoadSceneRequest</c>) — so re-tag, texture rehydration, <c>DrawComponent</c> restore, and
    /// camera-rig re-sync are all SHARED with the file load path (pre-mortem #2), never
    /// re-implemented. Wired by the overlay.</summary>
    public Action<SceneData>? RestoreSnapshot { get; set; }

    /// <summary>Captures the free editor VIEW (the live <c>Camera</c> position/zoom/rotation) so exit
    /// can restore exactly where the designer was looking. Wired by the overlay.</summary>
    public Func<CameraViewSnapshot>? CaptureView { get; set; }

    /// <summary>Restores a captured editor VIEW onto the live <c>Camera</c> — applied on exit AFTER
    /// the reader's auto-frame, so the captured Scene view wins. Wired by the overlay.</summary>
    public Action<CameraViewSnapshot>? RestoreView { get; set; }

    /// <summary>Snaps the free VIEW onto the camera rig (<c>Camera := rig state</c>) — the game-camera
    /// view adopted on Game-mode entry. Wired by the overlay to <c>EditorCameraRig.SnapViewToRig</c>.</summary>
    public Action? SnapViewToRig { get; set; }

    // Held while in Game mode; dropped on exit / Restart.
    private SceneData? _snapshot;
    private CameraViewSnapshot _snapshotView;

    /// <summary>Resume the game (Playing = <see cref="RunMode.Play"/>). No-op when already playing.
    /// <para><b>Auto-enter Game mode (UX2-F, pre-mortem #7).</b> Pressing Play while in
    /// <see cref="EditorViewMode.Scene"/> first enters Game mode — and the snapshot is taken
    /// <b>before</b> <see cref="GameState.RunMode"/> flips to Play, so no simulation frame can mutate
    /// the scene before it is captured. Pressing Play while already in Game mode does NOT re-snapshot
    /// (one snapshot per Game-mode session).</para></summary>
    public void Play(GameState state)
    {
        if (state.RunMode == RunMode.Play) return;
        if (ViewMode == EditorViewMode.Scene) EnterGameMode(state); // snapshot BEFORE the RunMode flip
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

    // ─── Scene / Game view-mode transitions (UX2-F) ────────────────────────────────────────────────

    /// <summary>The <c>[Scene | Game]</c> toggle: enter Game mode from Scene, or exit to Scene from
    /// Game (see <see cref="EnterGameMode"/> / <see cref="ExitToSceneMode"/>).</summary>
    public void ToggleViewMode(GameState state)
    {
        if (ViewMode == EditorViewMode.Scene) EnterGameMode(state);
        else ExitToSceneMode(state);
    }

    /// <summary>
    /// Enters the Game-mode sandbox (UX2-F): <b>snapshots the scene FIRST</b> —
    /// <see cref="CaptureSnapshot"/> (an in-memory <see cref="SceneData"/>, no file I/O) plus the
    /// current history dirty state and the Scene-mode VIEW — <b>before</b> anything can flip
    /// <see cref="GameState.RunMode"/> to Play (pre-mortem #7), then adopts the game-camera view
    /// (<see cref="SnapViewToRig"/>). No-op when already in Game mode (one snapshot per session).
    /// Does NOT itself change <see cref="GameState.RunMode"/> — the toggle enters while Paused; the
    /// Play button flips RunMode after this returns.
    /// </summary>
    public void EnterGameMode(GameState state)
    {
        if (ViewMode == EditorViewMode.Game) return;

        _snapshot = CaptureSnapshot?.Invoke();      // held in memory — the restore point
        SnapshotWasDirty = _history.IsDirty;         // the Scenes-panel ● reflects THIS while in Game
        _snapshotView = CaptureView?.Invoke() ?? default;

        ViewMode = EditorViewMode.Game;
        SnapViewToRig?.Invoke();                     // Camera := rig state (the authored game-camera view)

        Logger.Info("[level-editor] Transport: entered Game mode (sandbox) — scene snapshotted; " +
                    "edits discard on exit, Save blocked.");
    }

    /// <summary>
    /// Exits the Game-mode sandbox back to Scene mode (UX2-F): lands <b>Paused</b>
    /// (<see cref="RunMode.Edit"/>), disposes the sandbox scene entities (REUSING the Restart sweep —
    /// editor infrastructure / cursor / KeepAlive survive), restores the snapshot <b>through the
    /// reader</b> (<see cref="RestoreSnapshot"/> — the shared restore path re-tags, rehydrates
    /// textures, restores <c>DrawComponent</c>s, and re-syncs the camera rig), then clears the undo
    /// history (undo after exit is a no-op — pre-mortem #3) and restores the captured dirty state and
    /// Scene VIEW. Sandbox edits vanish: <b>Scene mode always shows exactly what Save would write.</b>
    /// No-op when already in Scene mode.
    /// </summary>
    public void ExitToSceneMode(GameState state)
    {
        if (ViewMode == EditorViewMode.Scene) return;

        state.RunMode = RunMode.Edit;                // land Paused before restoring
        DisposeSceneEntities();                      // the SAME sweep Restart uses (infra/cursor/keep survive)

        if (_snapshot != null) RestoreSnapshot?.Invoke(_snapshot); // through the reader (shared path)

        _history.Clear();                            // restored entities invalidate old commands (Restart rule)
        if (SnapshotWasDirty) _history.MarkDirty();  // restore the captured dirty state
        RestoreView?.Invoke(_snapshotView);          // override the reader's auto-frame with the Scene view

        _snapshot = null;
        ViewMode = EditorViewMode.Scene;

        Logger.Info("[level-editor] Transport: exited Game mode — sandbox discarded, scene restored " +
                    "from the snapshot. Paused.");
    }

    /// <summary>
    /// Return the world to the state of the original load (see the class doc for the exact
    /// sequence and the survival boundary). Lands Paused. Unsaved edits are discarded.
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

        // UX2-F: Restart also lands in Scene mode with the snapshot dropped. The snapshot IS an unsaved
        // edit, and Restart's contract is "discards unsaved edits" — so no special case: the disk reload
        // below is the source of truth, and the sandbox snapshot is simply forgotten.
        _snapshot = null;
        SnapshotWasDirty = false;
        ViewMode = EditorViewMode.Scene;

        // The world-level level components must go BEFORE the re-publish: the LDtk parsers react
        // to CurrentLevelComponent ADDED (a Set over a present component fires Changed instead).
        _world.Remove<CurrentLevelComponent>();
        _world.Remove<CurrentBackgroundColorComponent>();

        DisposeSceneEntities();
        Reload();

        Logger.Info("[level-editor] Transport: Restart — scene rebuilt from the original load " +
                    "request; unsaved edits (incl. any Game-mode sandbox) discarded. Scene mode, Paused.");
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

    /// <summary>Whether <paramref name="entity"/> survives a restart sweep (see the class doc).</summary>
    public bool Survives(Entity entity) =>
        entity.Has<EditorInfrastructureComponent>()
        || entity.Has<CursorControllerComponent>()
        || entity.Has<CursorInputComponent>()
        || IsKeptByScreen(entity);

    private bool IsKeptByScreen(Entity entity)
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

/// <summary>
/// The editor's view mode (UX2-F), owned by <see cref="EditorTransport"/> alongside
/// <see cref="RunMode"/>. <see cref="Scene"/> edits the real scene through the free editor view;
/// <see cref="Game"/> is a Unity-style sandbox looking through the game camera whose edits are
/// discarded on exit. Default <see cref="Scene"/>.
/// </summary>
public enum EditorViewMode
{
    /// <summary>Edit the real scene through the free editor view (default).</summary>
    Scene,
    /// <summary>Sandbox: look through the game camera; edits discard on exit; Save blocked.</summary>
    Game,
}

/// <summary>A snapshot of the free editor VIEW (the live <c>Camera</c>) — position / zoom / rotation —
/// captured on Game-mode entry and restored on exit so the designer returns to exactly where they were
/// looking. Plain value data.</summary>
public readonly struct CameraViewSnapshot(Vector2 position, float zoom, float rotation)
{
    public readonly Vector2 Position = position;
    public readonly float Zoom = zoom;
    public readonly float Rotation = rotation;
}
