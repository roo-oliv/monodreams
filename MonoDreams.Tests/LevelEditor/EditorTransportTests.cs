using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the editor <b>transport model</b>: under the editor run configuration the editor is
/// always composed and visible — there is no key that toggles it away — and the designer drives
/// the game through transport controls instead: <b>Play/Pause</b> (one toggle mapping onto
/// <see cref="GameState.RunMode"/>: Paused = <see cref="RunMode.Edit"/>, Playing =
/// <see cref="RunMode.Play"/>) and <b>Restart</b> (return the world to the state of the ORIGINAL
/// load: dispose every scene entity — editor infrastructure, the cursor pipeline, and
/// screen-kept entities survive — clear the world-level level components, re-run the screen's
/// recorded reload, and land Paused; unsaved live edits are DISCARDED).
///
/// <para>Also covers the toolbar transport buttons (left-most, dispatch in BOTH modes while the
/// tool/save/undo buttons stay Paused-only) and the headless editor-op transport ops
/// (<c>Play</c>/<c>Pause</c>/<c>Restart</c> replacing the retired <c>ToggleMode</c>).</para>
/// </summary>
public class EditorTransportTests
{
    private static GameState Playing() => new(new GameTime()) { RunMode = RunMode.Play };
    private static GameState Paused() => new(new GameTime()) { RunMode = RunMode.Edit };

    private sealed class CountingSystem : ISystem<GameState>
    {
        public int Runs;
        public bool IsEnabled { get; set; } = true;
        public void Update(GameState state) => Runs++;
        public void Dispose() { }
    }

    private static (EditorTransport transport, EditorHistory history) MakeTransport(World world)
    {
        var history = new EditorHistory(world);
        return (new EditorTransport(world, history), history);
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    // ---- Play/Pause maps onto RunMode; boot under the flag is Paused ----

    [Fact]
    public void BootUnderTheRunFlag_IsPaused()
    {
        // The transport's Paused state IS RunMode.Edit — the flag boots straight into it.
        Assert.Equal(RunMode.Edit, EditorRunFlag.InitialRunMode(true));
        Assert.Equal(RunMode.Play, EditorRunFlag.InitialRunMode(false));
    }

    [Fact]
    public void PlayPause_TogglesRunModeInPlace_FreezeGatedSystemsFollow()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);

        var logic = new CountingSystem(); // stands in for the frozen game-logic block
        var gate = new GatedSystem(logic, EditTimeBehavior.Freeze);
        var state = Paused(); // boot state under the flag

        gate.Update(state);
        Assert.Equal(0, logic.Runs); // Paused (Edit): Freeze-gated logic skips

        transport.Play(state);
        Assert.Equal(RunMode.Play, state.RunMode);
        gate.Update(state);
        Assert.Equal(1, logic.Runs); // Playing: the game runs

        transport.Pause(state);
        Assert.Equal(RunMode.Edit, state.RunMode);
        gate.Update(state);
        Assert.Equal(1, logic.Runs); // frozen again

        // The toggle form flips whichever state is current.
        transport.TogglePlayPause(state);
        Assert.Equal(RunMode.Play, state.RunMode);
        transport.TogglePlayPause(state);
        Assert.Equal(RunMode.Edit, state.RunMode);
    }

    // ---- Restart: dispose scene entities, keep editor infrastructure, re-run the load, Paused ----

    [Fact]
    public void Restart_DisposesSceneEntities_KeepsEditorInfrastructureAndCursor_ReloadsAndPauses()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);

        // Editor infrastructure: chrome-style tagged entity + the cursor pipeline entity.
        var chrome = world.CreateEntity();
        chrome.Set(new EditorInfrastructureComponent());
        var cursor = MakeCursor(world);

        // The screen's recorded load: creates the scene root (with a child) at its loaded state.
        Entity sceneRoot = default;
        Entity sceneChild = default;
        var loads = 0;
        transport.Reload = () =>
        {
            loads++;
            sceneRoot = world.CreateEntity();
            sceneRoot.Set(new TransformComponent(new Vector2(10, 20)));
            sceneChild = world.CreateEntity();
            sceneChild.Set(new TransformComponent(new Vector2(1, 1)));
            sceneChild.SetParent(sceneRoot);
        };
        transport.Reload(); // the original load
        var originalRoot = sceneRoot;
        var originalChild = sceneChild;

        var state = Playing(); // restart while PLAYING must also work…
        transport.Restart(state);

        // …and lands Paused (the predictable state to hand back to the designer).
        Assert.Equal(RunMode.Edit, state.RunMode);
        Assert.Equal(2, loads); // the original load request was re-published

        // The old scene sub-graph is gone; the reload created a fresh one at the loaded state.
        Assert.False(originalRoot.IsAlive);
        Assert.False(originalChild.IsAlive);
        Assert.True(sceneRoot.IsAlive);
        Assert.Equal(new Vector2(10, 20), sceneRoot.Get<TransformComponent>().Position);

        // Editor infrastructure and the cursor pipeline survive.
        Assert.True(chrome.IsAlive);
        Assert.True(cursor.IsAlive);
    }

    [Fact]
    public void Restart_DiscardsUnsavedEdits_AndClearsTheUndoHistory()
    {
        using var world = new World();
        var (transport, history) = MakeTransport(world);

        Entity scene = default;
        transport.Reload = () =>
        {
            scene = world.CreateEntity();
            scene.Set(new TransformComponent(new Vector2(10, 20)));
        };
        transport.Reload();

        // An unsaved live edit through the shared history (the gizmo path).
        history.Push(TransformEditCommand.FromCurrent(
            scene, new Vector2(99, 99), 0f, Vector2.One, Vector2.Zero));
        Assert.Equal(new Vector2(99, 99), scene.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count);

        transport.Restart(Paused());

        // The edit is DISCARDED: the scene is back at the loaded value…
        Assert.Equal(new Vector2(10, 20), scene.Get<TransformComponent>().Position);
        // …and the history is empty (its entries referenced disposed entities): undo is a no-op.
        Assert.Equal(0, history.Count);
        Assert.Equal(0, history.RedoCount);
        history.Undo(); // must not throw, must not touch anything
        Assert.Equal(new Vector2(10, 20), scene.Get<TransformComponent>().Position);
    }

    [Fact]
    public void Restart_RemovesTheWorldLevelComponents()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);
        transport.Reload = () => { };

        // The LDtk parsers subscribe to CurrentLevelComponent ADDED — a re-publish over a still-set
        // component would fire Changed instead, so restart must remove it first.
        world.Set(new CurrentLevelComponent(null));
        world.Set(new CurrentBackgroundColorComponent(Color.Black));

        transport.Restart(Paused());

        Assert.False(world.Has<CurrentLevelComponent>());
        Assert.False(world.Has<CurrentBackgroundColorComponent>());
    }

    [Fact]
    public void Restart_WithoutARecordedReload_IsALoudNoOp()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);

        var scene = world.CreateEntity();
        scene.Set(new TransformComponent(new Vector2(5, 5)));

        var state = Playing();
        transport.Restart(state); // no Reload registered

        // Nothing was torn down (a reloadless teardown would leave a blank world) and the mode is untouched.
        Assert.True(scene.IsAlive);
        Assert.Equal(RunMode.Play, state.RunMode);
    }

    [Fact]
    public void Restart_KeepAlivePredicate_ProtectsScreenInfrastructure_AndItsDescendants()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);
        transport.Reload = () => { };

        // Screen infrastructure a system created once at construction (e.g. the dialogue UI):
        // the screen names its root via KeepAlive; ChildOf descendants are kept with it.
        var keptRoot = world.CreateEntity();
        keptRoot.Set(new TransformComponent(Vector2.Zero));
        var keptChild = world.CreateEntity();
        keptChild.Set(new TransformComponent(Vector2.Zero));
        keptChild.SetParent(keptRoot);

        var sceneEntity = world.CreateEntity();
        sceneEntity.Set(new TransformComponent(Vector2.Zero));

        transport.KeepAlive = e => e == keptRoot;
        transport.Restart(Paused());

        Assert.True(keptRoot.IsAlive);
        Assert.True(keptChild.IsAlive); // kept transitively through the ChildOf parent chain
        Assert.False(sceneEntity.IsAlive);
    }

    // ---- The transport owns BOTH RunMode and the Scene/Game ViewMode (UX2-F, one owner) ----

    [Fact]
    public void Transport_OwnsViewMode_DefaultScene_ToggleEntersAndExits_ExitLandsPaused()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);

        // Default is Scene mode (the boot view mode alongside the boot RunMode).
        Assert.Equal(ViewportContextKind.Scene, transport.ActiveContextKind);

        // The toggle enters Game mode (the snapshot seams are unwired here → a graceful no-snapshot
        // toggle; the enter/exit content behaviour is covered by EditorGameModeTests with the seams).
        var state = Paused();
        transport.ToggleViewMode(state);
        Assert.Equal(ViewportContextKind.Game, transport.ActiveContextKind);

        // A toggle back to Scene lands Paused (Edit) even if the sandbox was Playing — the ONE owner
        // flips RunMode as part of the exit.
        transport.Play(state);
        Assert.Equal(RunMode.Play, state.RunMode);
        transport.ToggleViewMode(state);
        Assert.Equal(ViewportContextKind.Scene, transport.ActiveContextKind);
        Assert.Equal(RunMode.Edit, state.RunMode);
    }

    // ---- The transport buttons: the Scene panel header, live in BOTH modes; tools stay Paused-only ----

    [Fact]
    public void SceneHeader_LeadsWithTheTransportButtons()
    {
        // UX2-B: the transport relocated off the window bar to the Scene panel header, leading it;
        // UX2-C: the transform tools joined it in the header (transport cluster, then tool cluster).
        var header = Array.ConvertAll(EditorChromeBuilder.HeaderButtons, b => b.action);
        Assert.Equal(EditorToolbarAction.PlayPause, header[0]);
        Assert.Equal(EditorToolbarAction.Restart, header[1]);
        Assert.Contains(EditorToolbarAction.ToolMove, header);
        // The window bar keeps the remaining editing actions (no transport, no transform tools).
        var windowBar = Array.ConvertAll(EditorChromeBuilder.DefaultButtons, b => b.action);
        Assert.DoesNotContain(EditorToolbarAction.PlayPause, windowBar);
        Assert.DoesNotContain(EditorToolbarAction.ToolMove, windowBar);
        Assert.Contains(EditorToolbarAction.Save, windowBar);
    }

    private static Entity MakeToolbarButton(World world, EditorToolbarAction action, Rectangle bounds)
    {
        var label = world.CreateEntity();
        label.Set(new TransformComponent(Vector2.Zero));
        label.Set(new DynamicTextComponent { TextContent = "Pause" });

        var button = world.CreateEntity();
        button.Set(new TransformComponent(new Vector2(bounds.X, bounds.Y)));
        button.Set(new SimpleButtonComponent
        {
            Size = new Vector2(bounds.Width, bounds.Height),
            TextEntity = label,
        });
        button.Set(new ToolbarButtonComponent { Action = action, Bounds = bounds });
        return button;
    }

    private static void Click(Entity cursor, Vector2 screenPoint)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = screenPoint;
        input.LeftButtonReleased = true;
    }

    [Fact]
    public void ToolbarTransportButtons_DispatchInBothModes_ToolButtonsOnlyWhilePaused()
    {
        using var world = new World();
        var playPause = MakeToolbarButton(world, EditorToolbarAction.PlayPause, new Rectangle(0, 0, 60, 30));
        MakeToolbarButton(world, EditorToolbarAction.Restart, new Rectangle(70, 0, 60, 30));
        MakeToolbarButton(world, EditorToolbarAction.Save, new Rectangle(140, 0, 60, 30));
        var cursor = MakeCursor(world);

        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (action, _) => dispatched.Add(action));

        // While PLAYING: the transport buttons dispatch, the tool/save buttons are inert.
        Click(cursor, new Vector2(30, 15));
        toolbar.Update(Playing());
        Click(cursor, new Vector2(100, 15));
        toolbar.Update(Playing());
        Click(cursor, new Vector2(170, 15));
        toolbar.Update(Playing());
        Assert.Equal(new[] { EditorToolbarAction.PlayPause, EditorToolbarAction.Restart }, dispatched);

        // While PAUSED: everything dispatches.
        dispatched.Clear();
        Click(cursor, new Vector2(170, 15));
        toolbar.Update(Paused());
        Assert.Equal(new[] { EditorToolbarAction.Save }, dispatched);

        // The Play/Pause button's label reflects the transport state (one toggle button).
        toolbar.Update(Paused());
        Assert.Equal("Play", playPause.Get<SimpleButtonComponent>().TextEntity!.Value
            .Get<DynamicTextComponent>().TextContent);
        toolbar.Update(Playing());
        Assert.Equal("Pause", playPause.Get<SimpleButtonComponent>().TextEntity!.Value
            .Get<DynamicTextComponent>().TextContent);
    }

    // ---- Headless transport ops: Play / Pause / Restart drive the transport with no mouse ----

    [Fact]
    public void EditorOps_PlayPauseRestart_DriveTheTransportHeadlessly()
    {
        using var world = new World();
        var (transport, _) = MakeTransport(world);
        MakeCursor(world);

        Entity scene = default;
        var loads = 0;
        transport.Reload = () =>
        {
            loads++;
            scene = world.CreateEntity();
            scene.Set(new TransformComponent(new Vector2(10, 20)));
        };
        transport.Reload();

        var exits = 0;
        var plan = new EditorOpPlan
        {
            TailFrames = 0,
            Ops = new List<EditorOp>
            {
                new() { Frame = 0, Kind = EditorOpKind.Play },
                new() { Frame = 1, Kind = EditorOpKind.Pause },
                new() { Frame = 2, Kind = EditorOpKind.Restart },
            },
        };
        using var driver = new EditorOpReplaySystem(
            world, plan, dispatch: null, requestExit: () => exits++, transport: transport);

        var state = Paused(); // boot state under the flag

        driver.Update(state);
        Assert.Equal(RunMode.Play, state.RunMode); // Play op

        driver.Update(state);
        Assert.Equal(RunMode.Edit, state.RunMode); // Pause op

        driver.Update(state);
        Assert.Equal(RunMode.Edit, state.RunMode); // Restart lands Paused
        Assert.Equal(2, loads);                    // …and re-ran the recorded load
        Assert.True(scene.IsAlive);

        driver.Update(state); // drain + tail elapsed → exit requested exactly once
        Assert.Equal(1, exits);
    }
}
