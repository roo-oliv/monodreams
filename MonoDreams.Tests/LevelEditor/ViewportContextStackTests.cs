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
        public bool LastRestoreSuppressedRig; // PF-D: RestoringPrefabContext observed during the last restore

        public Harness()
        {
            History = new EditorHistory(World);
            Stack = new ViewportContextStack(History, Shell, "island")
            {
                CaptureSnapshot = () => { Captures++; Log.Add("snapshot"); return new SceneData { Version = WorldState }; },
                RestoreSnapshot = data => { LastRestoreSuppressedRig = Stack.RestoringPrefabContext; Log.Add("restore"); WorldState = data.Version; },
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

    // ─── PF-D: prefab-context tabs ────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenPrefab_SnapshotsActive_PushesPrefabTab_RestoresRigSuppressed()
    {
        var h = new Harness();
        h.WorldState = 3;

        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 77 });

        Assert.Equal(1, h.Captures);              // the Scene context was snapshotted (preserved, not discarded)
        Assert.Equal(1, h.Sweeps);
        Assert.True(h.LastRestoreSuppressedRig);  // pre-mortem #8: the prefab restore suppressed the camera rig
        Assert.Equal(77, h.WorldState);           // the prefab's content was loaded
        Assert.Equal(ViewportContextKind.Prefab, h.Stack.ActiveKind);
        Assert.Equal(2, h.Stack.Contexts.Count);
        Assert.Equal("npc", h.Stack.Active.Id);
        Assert.True(h.Stack.Contexts[1].Closable);
        Assert.False(h.Stack.RestoringPrefabContext); // cleared after the synchronous restore
        Assert.False(h.History.IsDirty);          // a fresh prefab context is clean
    }

    [Fact]
    public void OpenPrefab_SameId_Twice_JustActivates_OneTab()
    {
        var h = new Harness();
        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 10 });
        h.Stack.SwitchTo(0); // back to the Scene tab (the prefab tab persists in the background)
        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 10 });

        Assert.Equal(2, h.Stack.Contexts.Count);  // still ONE prefab tab, re-activated
        Assert.Equal(ViewportContextKind.Prefab, h.Stack.ActiveKind);
    }

    [Fact]
    public void SwitchToBackgroundedPrefab_RestoresRigSuppressed()
    {
        var h = new Harness();
        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 77 });
        h.Stack.SwitchTo(0);                      // to the Scene (a Scene restore → rig NOT suppressed)
        Assert.False(h.LastRestoreSuppressedRig);
        h.Stack.SwitchTo(1);                      // back to the prefab (rig suppressed again)
        Assert.True(h.LastRestoreSuppressedRig);
        Assert.Equal(ViewportContextKind.Prefab, h.Stack.ActiveKind);
    }

    [Fact]
    public void PrefabTab_DecideClose_DirtyConfirms_CleanCloses_SceneRefused()
    {
        var h = new Harness();
        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 10 });

        Assert.Equal(ViewportCloseDecision.CloseClean, h.Stack.DecideClose(1)); // clean prefab tab
        Assert.Equal(ViewportCloseDecision.Refused, h.Stack.DecideClose(0));    // the Scene tab, never closable

        h.History.Push(new NoopCommand());        // dirty the active prefab tab
        Assert.Equal(ViewportCloseDecision.ConfirmDirty, h.Stack.DecideClose(1));
    }

    [Fact]
    public void CloseCleanContext_ActivePrefab_ReturnsToScene()
    {
        var h = new Harness();
        h.WorldState = 3;
        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 77 });

        h.Stack.CloseCleanContext(1);

        Assert.Single(h.Stack.Contexts);          // the prefab tab is gone
        Assert.Equal(ViewportContextKind.Scene, h.Stack.ActiveKind);
        Assert.Equal(3, h.WorldState);            // the Scene content is restored
    }

    [Fact]
    public void PrefabTab_PerContextDirty_IsolatedFromTheScene()
    {
        var h = new Harness();
        h.History.Push(new NoopCommand());        // the Scene is dirty
        Assert.True(h.History.IsDirty);

        h.Stack.OpenPrefab("npc", "npc", new SceneData { Version = 77 });
        Assert.False(h.History.IsDirty);          // the prefab context is its own clean save-point

        h.Stack.SwitchTo(0);                      // back to the Scene
        Assert.True(h.History.IsDirty);           // the Scene's dirtiness is restored (per-context isolation)
    }

    // ─────────────── TB-A: named per-scene tabs, multi-tab switch, per-tab dirty, close gates ─────────

    [Fact]
    public void SceneTab_IsTitledByItsSceneId_NotTheWordScene()
    {
        var h = new Harness();
        Assert.Equal("island", h.Shell.ViewportTabs[0].Label); // the scene id, not "Scene"
        h.Stack.SetActiveSceneId("island2");
        Assert.Equal("island2", h.Stack.Active.Id);
        Assert.Equal("island2", h.Shell.ViewportTabs[0].Label);
    }

    [Fact]
    public void AddSceneContext_OpensASecondNamedTab_NotYetActive_MakesBothClosable()
    {
        var h = new Harness();
        var idx = h.Stack.AddSceneContext("island2", "Game", "island2");
        Assert.Equal(1, idx);
        Assert.Equal(2, h.Stack.Contexts.Count);
        Assert.Equal(0, h.Stack.ActiveIndex);          // AddSceneContext does not activate
        Assert.Equal(2, h.Stack.SceneTabCount);
        Assert.Equal("island2", h.Shell.ViewportTabs[1].Label);
        Assert.Equal(1, h.Stack.IndexOfSceneTab("island2"));
        Assert.Equal(-1, h.Stack.IndexOfSceneTab("nope"));
        // With two scene tabs open, BOTH become closable in the strip (the last one refuses at close).
        Assert.True(h.Shell.ViewportTabs[0].Closable);
        Assert.True(h.Shell.ViewportTabs[1].Closable);
    }

    [Fact]
    public void TwoSceneTabs_SwitchFreely_EditBoth_PerTabDirty_NeverDiscards()
    {
        var h = new Harness();
        h.WorldState = 10;                          // island (tab 0) content
        var b = h.Stack.AddSceneContext("island2", "Game", "island2");
        h.Stack.SwitchTo(b);                        // island snapshotted(10, clean), swept; island2 clean
        h.WorldState = 20;                          // "edit" island2 content
        h.History.Push(new NoopCommand());          // dirty island2
        h.Stack.SwitchTo(0);                        // leave island2 (snapshot 20 + dirty), restore island(10)
        Assert.Equal(10, h.WorldState);             // island's snapshot restored (never discarded)
        Assert.False(h.History.IsDirty);            // island was clean
        h.Stack.SwitchTo(b);                        // back to island2 — its snapshot(20) + dirty reproduced
        Assert.Equal(20, h.WorldState);
        Assert.True(h.History.IsDirty);             // island2's per-tab dirty restored
    }

    [Fact]
    public void DecideClose_LastSceneTab_Refused_ButClosableOnceASecondIsOpen()
    {
        var h = new Harness();
        Assert.Equal(ViewportCloseDecision.Refused, h.Stack.DecideClose(0)); // the only scene tab
        h.Stack.AddSceneContext("island2", "Game", "island2");
        Assert.Equal(ViewportCloseDecision.CloseClean, h.Stack.DecideClose(0)); // no longer the last
        Assert.Equal(ViewportCloseDecision.CloseClean, h.Stack.DecideClose(1));
    }

    [Fact]
    public void DecideClose_DirtySceneTab_ConfirmDirty()
    {
        var h = new Harness();
        h.Stack.AddSceneContext("island2", "Game", "island2");
        h.History.Push(new NoopCommand());          // dirty the active (island, index 0)
        Assert.Equal(ViewportCloseDecision.ConfirmDirty, h.Stack.DecideClose(0));
    }

    [Fact]
    public void CloseCleanContext_ActiveSceneTab_ReturnsToNeighbour_RestoresIt()
    {
        var h = new Harness();
        var b = h.Stack.AddSceneContext("island2", "Game", "island2");
        h.WorldState = 10;                          // island content
        h.Stack.SwitchTo(b);                        // island snapshotted(10), on island2
        h.WorldState = 20;
        h.Log.Clear();
        h.Stack.CloseCleanContext(b);               // close island2 (active) → back to island + restore
        Assert.Single(h.Stack.Contexts);
        Assert.Equal(0, h.Stack.ActiveIndex);
        Assert.Equal(10, h.WorldState);             // island restored
        Assert.Contains("restore", h.Log);
    }

    [Fact]
    public void CloseCleanContext_BackgroundSceneTab_JustRemoved_NoWorldChange()
    {
        var h = new Harness();
        h.Stack.AddSceneContext("island2", "Game", "island2"); // index 1, background (active stays 0)
        h.Log.Clear();
        h.Stack.CloseCleanContext(1);               // background close
        Assert.Single(h.Stack.Contexts);
        Assert.Equal(0, h.Stack.ActiveIndex);
        Assert.Empty(h.Log);                        // no sweep/restore for a background close
    }

    [Fact]
    public void PrepareCrossScreenActivation_SnapshotsAPersistentLeaving_AimsAtTheTarget_NoSweep()
    {
        var h = new Harness();
        h.WorldState = 10;
        var b = h.Stack.AddSceneContext("island2", "Game", "island2");
        h.Log.Clear();
        h.Stack.PrepareCrossScreenActivation(b);    // the overlay's cross-screen prep (no sweep/restore)
        Assert.Equal(new[] { "snapshot" }, h.Log);  // the leaving island was preserved, NOT swept
        Assert.Equal(b, h.Stack.ActiveIndex);       // aimed at the target
        Assert.NotNull(h.Stack.Contexts[0].Snapshot); // island's snapshot captured for the return
    }

    [Fact]
    public void PrepareCrossScreenActivation_DropsALeavingGameTab_NeverSnapshotsIt()
    {
        var h = new Harness();
        h.Stack.EnterGame();                        // active = the discard Game tab
        var origin = h.Stack.GameOriginIndex;       // the scene it was spawned from (0)
        h.Log.Clear();
        h.Stack.PrepareCrossScreenActivation(origin);
        Assert.DoesNotContain("snapshot", h.Log);   // the Game tab is discard — never re-snapshotted
        Assert.Single(h.Stack.Contexts);            // the Game tab was dropped
        Assert.Equal(ViewportContextKind.Scene, h.Stack.ActiveKind);
    }

    [Fact]
    public void RestoreActiveSnapshot_RestoresThroughTheReader_WithoutSweeping()
    {
        var h = new Harness();
        h.WorldState = 10;
        var b = h.Stack.AddSceneContext("island2", "Game", "island2");
        h.Stack.SwitchTo(b);                        // island snapshotted(10)
        h.WorldState = 20;
        h.Stack.PrepareCrossScreenActivation(0);    // aim at island (snapshot present)
        h.Log.Clear();
        h.WorldState = 999;                          // a fresh screen load we must REPLACE, not double
        h.Stack.RestoreActiveSnapshot();            // the cross-screen restore (no sweep)
        Assert.Equal(10, h.WorldState);             // island restored (not 999)
        Assert.DoesNotContain("sweep", h.Log);      // no sweep — the screen skipped its fresh load
        Assert.Contains("restore", h.Log);
    }

    private sealed class NoopCommand : IEditorCommand
    {
        public void Apply(World world) { }
        public void Revert(World world) { }
    }
}
