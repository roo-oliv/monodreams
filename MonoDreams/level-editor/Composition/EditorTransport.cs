#nullable enable
using System;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's <b>transport</b>: the one owner of <see cref="GameState.RunMode"/> under the editor
/// run configuration. With the editor composed, the shell and chrome are ALWAYS visible — no key
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

    /// <summary>Resume the game (Playing = <see cref="RunMode.Play"/>). No-op when already playing.</summary>
    public void Play(GameState state)
    {
        if (state.RunMode == RunMode.Play) return;
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

        // The world-level level components must go BEFORE the re-publish: the LDtk parsers react
        // to CurrentLevelComponent ADDED (a Set over a present component fires Changed instead).
        _world.Remove<CurrentLevelComponent>();
        _world.Remove<CurrentBackgroundColorComponent>();

        DisposeSceneEntities();
        Reload();

        Logger.Info("[level-editor] Transport: Restart — scene rebuilt from the original load " +
                    "request; unsaved edits discarded. Paused.");
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
