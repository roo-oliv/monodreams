using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the TB-A host-scoped <see cref="EditorSession"/> — the object (one per host, beside the
/// <c>ScreenController</c>) whose <see cref="ViewportContextStack"/> survives a screen switch, mirroring
/// how the shared <c>GameState</c> survives one. The tests drive the stack THROUGH the session with fake
/// snapshot/restore seams, and model a "screen switch" as a <see cref="ViewportContextStack.Rebind"/> to a
/// FRESH world + shell (a new screen instance): the tab list + per-tab snapshots must survive, and a
/// cross-screen activation must restore the target snapshot through the reader exactly once (pre-mortem #2,
/// no double content).
/// </summary>
public class EditorSessionTests
{
    private sealed class Harness
    {
        public readonly EditorSession Session = new("island", "LevelSelection");
        public World World = new();
        public EditorHistory History = null!;
        public EditorShellStateComponent Shell = new();
        public int WorldState = 1;
        public int Restores;
        public int Sweeps;

        public Harness() => Bind();

        // Simulate a screen's overlay binding the host session's stack (the transport's Rebind + seam wiring).
        private void Bind()
        {
            History = new EditorHistory(World);
            var stack = Session.Stack;
            stack.Rebind(History, Shell);
            stack.CaptureSnapshot = () => new SceneData { Version = WorldState };
            stack.RestoreSnapshot = d => { Restores++; WorldState = d.Version; };
            stack.CaptureView = () => new CameraViewSnapshot(new Vector2(5, 5), 1f, 0f);
            stack.RestoreView = _ => { };
            stack.SweepSceneEntities = () => Sweeps++;
            stack.SnapViewToCameraEntity = () => { };
        }

        // A host screen switch: tear down the world + shell, bind a new one (the new screen's overlay).
        public void SwitchScreen(int worldStateAfterFreshLoad)
        {
            World = new World();
            Shell = new EditorShellStateComponent();
            WorldState = worldStateAfterFreshLoad;
            Bind();
        }
    }

    [Fact]
    public void Session_HoldsTheStack_SeedsTheBootSceneTab_PendingDefaultsOff()
    {
        var s = new EditorSession("island", "LevelSelection");
        Assert.Single(s.Stack.Contexts);
        Assert.Equal("island", s.Stack.SceneContext.Id);
        Assert.Equal("island", s.Stack.SceneContext.Label);        // titled by scene id (TB-A)
        Assert.Equal("LevelSelection", s.Stack.SceneContext.ScreenName);
        Assert.False(s.PendingActivation);
    }

    [Fact]
    public void TabList_SurvivesAScreenSwitch_ViaRebind()
    {
        var h = new Harness();
        // Open a second scene tab (island2 on the Game host) and aim at it for a cross-screen switch.
        var b = h.Session.Stack.AddSceneContext("island2", "Game", "island2");
        h.Session.Stack.PrepareCrossScreenActivation(b);
        Assert.Equal(2, h.Session.Stack.Contexts.Count);
        Assert.Equal(b, h.Session.Stack.ActiveIndex);

        // The host tears down the outgoing screen's world + shell and binds a fresh one — the session's
        // tab list must survive (the whole point of host-scoping it).
        h.SwitchScreen(worldStateAfterFreshLoad: 0);

        Assert.Equal(2, h.Session.Stack.Contexts.Count);           // tabs survived the switch
        Assert.Equal("island2", h.Session.Stack.Active.Id);
        Assert.Equal(2, h.Shell.ViewportTabs.Count);               // re-synced onto the NEW shell
        Assert.Equal(1, h.Shell.ActiveViewportTab);
    }

    [Fact]
    public void CrossScreenActivation_RestoresTheTargetSnapshot_Once_NoSweep_NoDoubleContent()
    {
        var h = new Harness();
        h.WorldState = 10;                                          // the boot (island) scene content
        var b = h.Session.Stack.AddSceneContext("island2", "Game", "island2");
        h.Session.Stack.SwitchTo(b);                               // island snapshotted(10), now on island2
        h.WorldState = 20;

        // Cross-screen activate island (index 0): snapshot island2, aim at island (its snapshot is present).
        h.Session.Stack.PrepareCrossScreenActivation(0);
        Assert.NotNull(h.Session.Stack.Active.Snapshot);

        // The new screen boots; because a snapshot is present the screen SKIPS its own fresh load (modelled
        // by NOT populating a fresh WorldState) and the overlay restores through the reader.
        h.SwitchScreen(worldStateAfterFreshLoad: 999);             // 999 = a stray fresh load we must NOT keep
        h.Restores = 0;
        h.Sweeps = 0;
        h.Session.Stack.RestoreActiveSnapshot();                   // the overlay's RestorePendingActivation path

        Assert.Equal(10, h.WorldState);                            // island restored (NOT the 999 fresh load)
        Assert.Equal(1, h.Restores);                               // restored exactly once
        Assert.Equal(0, h.Sweeps);                                 // no sweep — no double content to clear
    }

    [Fact]
    public void GameTab_FollowsAScreenSwitch_TabStaysActive_NoRestore()
    {
        var h = new Harness();
        h.WorldState = 10;
        h.Session.Stack.EnterGame();                               // spawn the Game tab (sandbox)
        Assert.Equal(ViewportContextKind.Game, h.Session.Stack.ActiveKind);

        // A gameplay transition (a ScreenTransitionRequest → LoadScreen) sets NO pending activation, so the
        // new screen loads fresh and the Game tab stays active — the session simply survives the switch.
        h.SwitchScreen(worldStateAfterFreshLoad: 77);              // the gameplay screen's fresh content
        h.Restores = 0;

        Assert.False(h.Session.PendingActivation);                 // a gameplay transition is not an activation
        Assert.Equal(ViewportContextKind.Game, h.Session.Stack.ActiveKind); // the Game tab still active
        Assert.Equal(2, h.Session.Stack.Contexts.Count);           // the boot scene tab is untouched behind it
        Assert.Equal(0, h.Restores);                               // no restore — gameplay owns the world
        Assert.Equal(77, h.WorldState);                            // the fresh gameplay content stands
    }
}
