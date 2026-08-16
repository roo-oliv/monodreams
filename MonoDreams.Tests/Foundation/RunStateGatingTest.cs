using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the foundation run-state model that the in-game level editor stands on
/// (see <c>docs/CORE_TENETS.md</c> — "The editor is part of the game", and the
/// foundation premises "Default RunMode=Play preserves all existing pipelines" and
/// "Edit-time behaviour is a per-system policy honoured by <c>GatedSystem</c>").
///
/// Pure logic — no rendering or world needed. A fake <see cref="ISystem{GameState}"/>
/// counts how often its <c>Update</c> runs; wrapping it in a <see cref="GatedSystem"/>
/// lets us assert exactly which run modes forward to the child for each
/// <see cref="EditTimeBehavior"/>.
/// </summary>
public class RunStateGatingTest
{
    /// <summary>A minimal child system that records its run count and respects IsEnabled.</summary>
    private sealed class CountingSystem : ISystem<GameState>
    {
        public int UpdateCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool IsEnabled { get; set; } = true;

        public void Update(GameState state)
        {
            // Honour the ISystem contract: a disabled system does no work.
            if (!IsEnabled) return;
            UpdateCount++;
        }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>A child that OWNS transient entities: it counts the teardown calls the gate hands it
    /// (<see cref="ISuspendableSystem"/>) as well as its updates.</summary>
    private sealed class SuspendableSystem : ISystem<GameState>, ISuspendableSystem
    {
        public int UpdateCount { get; private set; }
        public int SuspendCount { get; private set; }
        public bool IsEnabled { get; set; } = true;

        public void Update(GameState state)
        {
            if (!IsEnabled) return;
            UpdateCount++;
        }

        public void Suspend(GameState state) => SuspendCount++;

        public void Dispose() { }
    }

    private static GameState NewState(RunMode mode)
    {
        var state = new GameState(new GameTime());
        state.RunMode = mode;
        return state;
    }

    // ---- Back-compat: the default run mode is Play (folds GatingBackCompatTest in) ----

    [Fact]
    public void GameState_RunMode_DefaultsToPlay()
    {
        // The single back-compat guarantee: a freshly constructed GameState — exactly what
        // ScreenController builds (new GameState(new GameTime())) — is in Play, so no
        // existing screen's gated systems change behaviour without an explicit opt-in.
        var state = new GameState(new GameTime());
        Assert.Equal(RunMode.Play, state.RunMode);
    }

    // ---- Freeze policy: runs in Play, skipped in Edit ----

    [Fact]
    public void Freeze_RunsInPlay_AndSkipsInEdit()
    {
        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.Freeze);

        gate.Update(NewState(RunMode.Play));
        Assert.Equal(1, child.UpdateCount); // Play: the child ran

        gate.Update(NewState(RunMode.Edit));
        Assert.Equal(1, child.UpdateCount); // Edit: skipped, still 1
    }

    // ---- RunNormally policy: runs in both modes ----

    [Fact]
    public void RunNormally_RunsInBothModes()
    {
        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally);

        gate.Update(NewState(RunMode.Play));
        gate.Update(NewState(RunMode.Edit));

        Assert.Equal(2, child.UpdateCount); // ran in Play and in Edit
    }

    // ---- Reserved policies behave as RunNormally for Wave 1 ----

    [Theory]
    [InlineData(EditTimeBehavior.RunPartial)]
    [InlineData(EditTimeBehavior.RuntimeEditable)]
    public void ReservedPolicies_RunInBothModes_ForNow(EditTimeBehavior policy)
    {
        var child = new CountingSystem();
        var gate = new GatedSystem(child, policy);

        gate.Update(NewState(RunMode.Play));
        gate.Update(NewState(RunMode.Edit));

        Assert.Equal(2, child.UpdateCount);
    }

    // ---- The pure decision table is directly assertable ----

    [Theory]
    [InlineData(EditTimeBehavior.RunNormally, RunMode.Play, true)]
    [InlineData(EditTimeBehavior.RunNormally, RunMode.Edit, true)]
    [InlineData(EditTimeBehavior.Freeze, RunMode.Play, true)]
    [InlineData(EditTimeBehavior.Freeze, RunMode.Edit, false)]
    [InlineData(EditTimeBehavior.RunPartial, RunMode.Edit, true)]
    [InlineData(EditTimeBehavior.RuntimeEditable, RunMode.Edit, true)]
    public void ShouldRun_MatchesTheGatingTable(EditTimeBehavior policy, RunMode mode, bool expected)
    {
        Assert.Equal(expected, GatedSystem.ShouldRun(policy, mode));
    }

    // ---- The gate honours its own IsEnabled ----

    [Fact]
    public void DisabledGate_NeverRunsTheChild()
    {
        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally) { IsEnabled = false };

        gate.Update(NewState(RunMode.Play));
        gate.Update(NewState(RunMode.Edit));

        Assert.Equal(0, child.UpdateCount);
    }

    // ---- The gate honours the CHILD's IsEnabled (composes, doesn't override) ----

    [Fact]
    public void Gate_HonoursChildIsEnabled()
    {
        var child = new CountingSystem { IsEnabled = false };
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally);

        gate.Update(NewState(RunMode.Play)); // gate + policy admit, but the child is disabled

        Assert.Equal(0, child.UpdateCount);
    }

    // ---- Stopping a child that owns transient entities (ISuspendableSystem) ----

    /// <summary>
    /// Skipping <c>Update</c> is not enough for a system that owns entities: it never gets another
    /// update in which to dispose them, and the draw stack (RunNormally) keeps rendering them. The
    /// gate therefore hands the child ONE Suspend on the running → not-running edge — not one per
    /// frozen frame — and another one after it has resumed and stopped again.
    /// </summary>
    [Fact]
    public void Freeze_SuspendsTheChild_OnceOnEachPlayToEditEdge()
    {
        var child = new SuspendableSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.Freeze);

        gate.Update(NewState(RunMode.Play));
        Assert.Equal(0, child.SuspendCount); // running: nothing to tear down

        gate.Update(NewState(RunMode.Edit));
        Assert.Equal(1, child.SuspendCount); // the Play → Pause edge

        gate.Update(NewState(RunMode.Edit));
        Assert.Equal(1, child.SuspendCount); // still frozen: not once per frame

        gate.Update(NewState(RunMode.Play)); // resumed — Suspend is a teardown, not a kill switch
        gate.Update(NewState(RunMode.Edit));
        Assert.Equal(2, child.UpdateCount);
        Assert.Equal(2, child.SuspendCount);
    }

    /// <summary>A gate that has never forwarded has nothing to tear down — a screen booting straight
    /// into Edit must not call Suspend on a child that has not run yet.</summary>
    [Fact]
    public void AGateThatNeverRan_SuspendsNothing()
    {
        var child = new SuspendableSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.Freeze);

        gate.Update(NewState(RunMode.Edit));

        Assert.Equal(0, child.SuspendCount);
    }

    /// <summary>The systems panel's master toggle stops the child in both modes, so it is the same
    /// edge: switching a running entry off tears its transient state down.</summary>
    [Fact]
    public void DisablingTheGate_SuspendsTheChild()
    {
        var child = new SuspendableSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally);

        gate.Update(NewState(RunMode.Play));
        gate.IsEnabled = false;
        gate.Update(NewState(RunMode.Play));

        Assert.Equal(1, child.UpdateCount);
        Assert.Equal(1, child.SuspendCount);
    }

    /// <summary>A plain child (the overwhelming majority) is unaffected: the gate never looks for a
    /// teardown it does not implement.</summary>
    [Fact]
    public void ANonSuspendableChild_IsSkippedSilently()
    {
        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.Freeze);

        gate.Update(NewState(RunMode.Play));
        gate.Update(NewState(RunMode.Edit));

        Assert.Equal(1, child.UpdateCount);
    }

    // ---- Dispose forwards to the child ----

    [Fact]
    public void Dispose_ForwardsToChild()
    {
        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.Freeze);

        gate.Dispose();

        Assert.Equal(1, child.DisposeCount);
    }
}
