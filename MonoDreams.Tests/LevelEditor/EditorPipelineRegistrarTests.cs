using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave-6 composition seam (level-editor premise "The pipeline registrar is the
/// composition seam"): <see cref="EditorPipelineRegistrar"/> wraps every entry in a
/// <see cref="GatedSystem"/> per its declared policy, preserves pipeline order, and retains a
/// runtime registry whose <c>SetEnabled</c> is the systems panel's toggle — off in BOTH modes.
/// Pure logic — no rendering or world needed.
/// </summary>
public class EditorPipelineRegistrarTests
{
    /// <summary>A minimal child system recording run counts (and optionally an execution log).</summary>
    private sealed class CountingSystem : ISystem<GameState>
    {
        private readonly Action? _onUpdate;
        public int UpdateCount { get; private set; }
        public bool IsEnabled { get; set; } = true;

        public CountingSystem(Action? onUpdate = null) => _onUpdate = onUpdate;

        public void Update(GameState state)
        {
            if (!IsEnabled) return;
            UpdateCount++;
            _onUpdate?.Invoke();
        }

        public void Dispose() { }
    }

    private static GameState NewState(RunMode mode)
    {
        var state = new GameState(new GameTime()) { RunMode = mode };
        return state;
    }

    // ---- Policy wrapping: entries gate per their declared EditTimeBehavior ----

    [Fact]
    public void FreezeEntry_RunsInPlay_SkipsInEdit()
    {
        var registrar = new EditorPipelineRegistrar();
        var logic = new CountingSystem();
        registrar.Add("logic", logic, EditTimeBehavior.Freeze);
        var pipeline = registrar.Build();

        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(1, logic.UpdateCount);

        pipeline.Update(NewState(RunMode.Edit));
        Assert.Equal(1, logic.UpdateCount); // frozen: skipped in Edit
    }

    [Fact]
    public void RunNormallyEntry_RunsInBothModes()
    {
        var registrar = new EditorPipelineRegistrar();
        var render = new CountingSystem();
        registrar.Add("render", render, EditTimeBehavior.RunNormally);
        var pipeline = registrar.Build();

        pipeline.Update(NewState(RunMode.Play));
        pipeline.Update(NewState(RunMode.Edit));
        Assert.Equal(2, render.UpdateCount);
    }

    // ---- The runtime toggle: SetEnabled stops the system in BOTH modes ----

    [Fact]
    public void SetEnabledFalse_StopsTheSystemInBothModes()
    {
        var registrar = new EditorPipelineRegistrar();
        var system = new CountingSystem();
        registrar.Add("hierarchy", system, EditTimeBehavior.RunNormally);
        var pipeline = registrar.Build();

        registrar.SetEnabled("hierarchy", false);
        pipeline.Update(NewState(RunMode.Play));
        pipeline.Update(NewState(RunMode.Edit));
        Assert.Equal(0, system.UpdateCount);
        Assert.False(registrar.IsEnabled("hierarchy"));

        // Re-enabling restores the policy-gated behaviour.
        registrar.SetEnabled("hierarchy", true);
        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(1, system.UpdateCount);
    }

    [Fact]
    public void SetEnabledFalse_OnAFreezeEntry_AlsoStopsItInPlay()
    {
        var registrar = new EditorPipelineRegistrar();
        var logic = new CountingSystem();
        registrar.Add("logic", logic, EditTimeBehavior.Freeze);
        var pipeline = registrar.Build();

        registrar.SetEnabled("logic", false);
        pipeline.Update(NewState(RunMode.Play)); // Freeze would run here — the toggle overrides
        Assert.Equal(0, logic.UpdateCount);
    }

    // ---- Order: enumeration and execution preserve registration order ----

    [Fact]
    public void Entries_PreserveRegistrationOrder_AndExecuteInOrder()
    {
        var registrar = new EditorPipelineRegistrar();
        var executed = new List<string>();
        registrar.Add("input", new CountingSystem(() => executed.Add("input")), EditTimeBehavior.RunNormally);
        registrar.Add("logic", new CountingSystem(() => executed.Add("logic")), EditTimeBehavior.Freeze);
        registrar.Add("render", new CountingSystem(() => executed.Add("render")), EditTimeBehavior.RunNormally);

        Assert.Equal(new[] { "input", "logic", "render" }, registrar.Entries.Select(e => e.Name));

        var pipeline = registrar.Build();
        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(new[] { "input", "logic", "render" }, executed);
    }

    // ---- Loud failures ----

    [Fact]
    public void SetEnabled_UnknownName_ThrowsLoudly()
    {
        var registrar = new EditorPipelineRegistrar();
        registrar.Add("input", new CountingSystem(), EditTimeBehavior.RunNormally);

        var ex = Assert.Throws<KeyNotFoundException>(() => registrar.SetEnabled("physics", false));
        Assert.Contains("physics", ex.Message);
        Assert.Contains("input", ex.Message); // the error lists what IS registered
    }

    [Fact]
    public void Add_DuplicateName_Throws()
    {
        var registrar = new EditorPipelineRegistrar();
        registrar.Add("logic", new CountingSystem(), EditTimeBehavior.Freeze);
        Assert.Throws<ArgumentException>(
            () => registrar.Add("logic", new CountingSystem(), EditTimeBehavior.RunNormally));
    }

    [Fact]
    public void Add_AfterBuild_Throws()
    {
        var registrar = new EditorPipelineRegistrar();
        registrar.Add("input", new CountingSystem(), EditTimeBehavior.RunNormally);
        registrar.Build();
        Assert.Throws<InvalidOperationException>(
            () => registrar.Add("late", new CountingSystem(), EditTimeBehavior.RunNormally));
    }

    // ---- The edit-mode default declaration (the systems panel's initial Edit column) ----

    [Fact]
    public void EnabledInEditByDefault_DerivesFromThePolicy()
    {
        var registrar = new EditorPipelineRegistrar();
        var frozen = registrar.Add("logic", new CountingSystem(), EditTimeBehavior.Freeze);
        var live = registrar.Add("hierarchy", new CountingSystem(), EditTimeBehavior.RunNormally);

        Assert.False(frozen.EnabledInEditByDefault);
        Assert.True(live.EnabledInEditByDefault);

        // An explicit declaration consistent with the policy is accepted.
        var explicitEntry = registrar.Add("render", new CountingSystem(), EditTimeBehavior.RunNormally,
            enabledInEditByDefault: true);
        Assert.True(explicitEntry.EnabledInEditByDefault);
    }

    [Fact]
    public void ExplicitEditDefault_ContradictingThePolicy_ThrowsLoudly()
    {
        // Honouring a per-mode default that differs from the policy needs the runtime policy
        // override (systems-panel follow-up); until then the registrar refuses loudly rather
        // than recording a declaration with no effect.
        var registrar = new EditorPipelineRegistrar();
        Assert.Throws<ArgumentException>(() =>
            registrar.Add("logic", new CountingSystem(), EditTimeBehavior.Freeze, enabledInEditByDefault: true));
        Assert.Throws<ArgumentException>(() =>
            registrar.Add("collision", new CountingSystem(), EditTimeBehavior.RunNormally, enabledInEditByDefault: false));
    }

    // ---- The registry exposes gate + child refs (what the systems panel renders) ----

    [Fact]
    public void Entry_ExposesPolicyGateAndChildRefs()
    {
        var registrar = new EditorPipelineRegistrar();
        var child = new CountingSystem();
        var entry = registrar.Add("logic", child, EditTimeBehavior.Freeze);

        Assert.Equal("logic", entry.Name);
        Assert.Equal(EditTimeBehavior.Freeze, entry.Policy);
        Assert.Same(child, entry.System);
        Assert.Same(child, entry.Gate.Child);
        Assert.True(entry.IsEnabled);
        Assert.Same(entry, registrar.GetEntry("logic"));
    }
}
