using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PF-B <b><see cref="ViewportContextStack"/></b> — the ONE viewport-tab switching mechanism
/// (pre-mortem #4; premise "The viewport context stack …"): spawning the Game tab snapshots the active
/// (Scene) context and keeps the live world as the sandbox; leaving it (a <see cref="ViewportContextStack.SwitchTo"/>
/// back to the Scene tab) sweeps + reader-restores the Scene, never re-snapshotting the discard Game tab,
/// and drops it from the strip. The dirty-close gate (<see cref="ViewportContextStack.DecideClose"/>)
/// refuses the Scene tab so a dirty Scene is never silently discarded, and the strip descriptors track the
/// contexts. Driven directly with fake snapshot/sweep seams (a real world only backs the history).
/// </summary>
public class ViewportContextStackTests
{
    /// <summary>A stack wired to in-memory fakes: the "scene" is a single int the capture round-trips
    /// (encoded in <see cref="SceneData.Version"/>), and every snapshot / sweep / restore appends to a
    /// log so the orchestration order is observable.</summary>
    private sealed class Harness
    {
        public readonly World World = new();
        public readonly EditorHistory History;
        public readonly EditorShellStateComponent Shell = new();
        public readonly ViewportContextStack Stack;
        public readonly List<string> Log = new();

        public int WorldState = 1;   // the live "scene" the capture/restore round-trips
        public int Captures;
        public int Sweeps;
        public int ViewSnaps;

        public Harness()
        {
            History = new EditorHistory(World);
            Stack = new ViewportContextStack(History, Shell, "island")
            {
                CaptureSnapshot = () => { Captures++; Log.Add("snapshot"); return new SceneData { Version = WorldState }; },
                RestoreSnapshot = data => { Log.Add("restore"); WorldState = data.Version; },
                CaptureView = () => new CameraViewSnapshot(new Microsoft.Xna.Framework.Vector2(5, 5), 1f, 0f),
                RestoreView = _ => { },
                SnapViewToRig = () => ViewSnaps++,
                SweepSceneEntities = () => { Sweeps++; Log.Add("sweep"); },
            };
        }
    }

    [Fact]
    public void FreshStack_HasOneSceneTab_ActiveNeverClosable()
    {
        var h = new Harness();
        Assert.Equal(ViewportContextKind.Scene, h.Stack.ActiveKind);
        Assert.Single(h.Stack.Contexts);
        Assert.False(h.Stack.SceneContext.Closable);
        // The strip descriptors were synced: one Scene tab, active.
        Assert.Single(h.Shell.ViewportTabs);
        Assert.Equal(ViewportContextKind.Scene, h.Shell.ViewportTabs[0].Kind);
        Assert.Equal(0, h.Shell.ActiveViewportTab);
    }

    [Fact]
    public void EnterGame_SnapshotsSceneAndAdoptsRig_NoSweep_KeepsLiveWorld_ThenTabAppears()
    {
        var h = new Harness();
        h.WorldState = 42;

        h.Stack.EnterGame();

        // Snapshot taken (the Scene restore point), rig view adopted, NO sweep (the live world IS the sandbox).
        Assert.Equal(new[] { "snapshot" }, h.Log);
        Assert.Equal(1, h.ViewSnaps);
        Assert.Equal(0, h.Sweeps);
        Assert.Equal(ViewportContextKind.Game, h.Stack.ActiveKind);
        Assert.Equal(42, h.WorldState); // unchanged — the sandbox is the live world
        // The Game tab appeared (closable, active) — the strip is descriptor-driven.
        Assert.Equal(2, h.Shell.ViewportTabs.Count);
        Assert.Equal(ViewportContextKind.Game, h.Shell.ViewportTabs[1].Kind);
        Assert.True(h.Shell.ViewportTabs[1].Closable);
        Assert.Equal(1, h.Shell.ActiveViewportTab);
    }

    [Fact]
    public void EnterGame_Twice_IsNoOp_OneSnapshotPerSession()
    {
        var h = new Harness();
        h.Stack.EnterGame();
        h.Stack.EnterGame(); // already on the Game tab — no re-snapshot, no second tab
        Assert.Equal(1, h.Captures);
        Assert.Equal(2, h.Stack.Contexts.Count);
    }

    [Fact]
    public void GameRoundTrip_SweepsAndRestoresScene_DropsGameTab_SceneStateSurvives()
    {
        var h = new Harness();
        h.WorldState = 7;         // the Scene state to preserve

        h.Stack.EnterGame();      // snapshots 7 into the Scene context
        h.WorldState = 99;        // sandbox churn
        h.Log.Clear();

        h.Stack.ExitToScene();    // sweep + restore the Scene (never snapshots the discard Game tab)

        Assert.Equal(new[] { "sweep", "restore" }, h.Log); // NO "snapshot" on leave (the Game tab is discard)
        Assert.Equal(7, h.WorldState);                     // the Scene state survived the round-trip
        Assert.Equal(ViewportContextKind.Scene, h.Stack.ActiveKind);
        Assert.Single(h.Stack.Contexts);                   // the Game tab disappeared (never persists)
        Assert.Single(h.Shell.ViewportTabs);
        Assert.Equal(0, h.Shell.ActiveViewportTab);
    }

    [Fact]
    public void ExitToScene_RestoresCapturedDirty_ClearsHistory()
    {
        var h = new Harness();
        // Pre-entry: a real recorded edit → dirty.
        h.History.Push(new NoopCommand());
        Assert.True(h.History.IsDirty);

        h.Stack.EnterGame();          // captures WasDirty = true onto the Scene context
        h.History.Push(new NoopCommand()); // sandbox churn
        h.Stack.ExitToScene();

        // The restored Scene dirtiness is the CAPTURED one (dirty), and the history is otherwise empty.
        Assert.True(h.History.IsDirty);
        Assert.Equal(0, h.History.Count);
        Assert.Equal(0, h.History.RedoCount);
    }

    [Fact]
    public void ResetToScene_DropsGameTab_AndForgetsTheSceneSnapshot()
    {
        var h = new Harness();
        h.Stack.EnterGame();
        Assert.NotNull(h.Stack.SceneContext.Snapshot);

        h.Stack.ResetToScene(); // the transport's Restart hook

        Assert.Equal(ViewportContextKind.Scene, h.Stack.ActiveKind);
        Assert.Single(h.Stack.Contexts);
        Assert.Null(h.Stack.SceneContext.Snapshot); // forgotten — a Restart reloads from disk
        // The next Game entry re-snapshots fresh.
        h.Captures = 0;
        h.Stack.EnterGame();
        Assert.Equal(1, h.Captures);
    }

    // ─────────────── The dirty-close gate (pre-mortem #9) ─────────────────────────────────────────────

    [Fact]
    public void DecideClose_RefusesTheSceneTab_AlwaysEvenWhenDirty()
    {
        var h = new Harness();
        h.History.Push(new NoopCommand()); // the Scene is dirty
        // The Scene tab (index 0) is never closable — a dirty Scene can never be silently discarded.
        Assert.Equal(ViewportCloseDecision.Refused, h.Stack.DecideClose(0));
        Assert.Equal(ViewportCloseDecision.Refused, h.Stack.DecideClose(-1));
        Assert.Equal(ViewportCloseDecision.Refused, h.Stack.DecideClose(5));
    }

    [Fact]
    public void DecideClose_TheGameTab_DiscardsImmediately_NoConfirm()
    {
        var h = new Harness();
        h.Stack.EnterGame();
        // The Game tab's × is discard-by-nature (its edits discard on leave), so no dirty confirm.
        Assert.Equal(ViewportCloseDecision.DiscardImmediately, h.Stack.DecideClose(1));
    }

    [Fact]
    public void DirtyScene_SurvivesAGameRoundTrip_NeverSilentlyDiscarded()
    {
        var h = new Harness();
        h.WorldState = 3;
        h.History.Push(new NoopCommand()); // the Scene is dirty before entering Game
        Assert.True(h.History.IsDirty);

        h.Stack.EnterGame();     // snapshots the (dirty) Scene — never discards it
        h.WorldState = 88;       // churn
        h.Stack.ExitToScene();   // restores the Scene + its captured dirtiness

        Assert.Equal(3, h.WorldState);      // the dirty Scene's content is back
        Assert.True(h.History.IsDirty);     // and it still reads dirty (nothing silently discarded)
    }

    private sealed class NoopCommand : IEditorCommand
    {
        public void Apply(World world) { }
        public void Revert(World world) { }
    }
}
