using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs.System;
using DefaultEcs.Threading;
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

    // ---- Groups: the registrar owns the hierarchy (named children behind one policy gate) ----

    [Fact]
    public void Group_Sequential_BuildsOneGateAroundTheChildren_FreezeFreezesTheWholeGroupInEdit()
    {
        var registrar = new EditorPipelineRegistrar();
        var executed = new List<string>();
        var a = new CountingSystem(() => executed.Add("a"));
        var b = new CountingSystem(() => executed.Add("b"));
        var group = registrar.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("a", a);
            g.Add("b", b);
        });
        var pipeline = registrar.Build();

        // The group entry IS the tree node: named, gated with the group policy, children visible.
        Assert.True(group.IsGroup);
        Assert.Equal(EditTimeBehavior.Freeze, group.Policy);
        Assert.Equal(new[] { "logic.a", "logic.b" }, group.Children.Select(c => c.Name));

        // Play: the whole group runs, children in registration order.
        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(new[] { "a", "b" }, executed);

        // Edit: the group's Freeze gate skips EVERY child (identical to the old opaque composite).
        pipeline.Update(NewState(RunMode.Edit));
        Assert.Equal(1, a.UpdateCount);
        Assert.Equal(1, b.UpdateCount);
    }

    [Fact]
    public void Group_ParallelKind_RunsAllChildren()
    {
        using var runner = new DefaultParallelRunner(1);
        var registrar = new EditorPipelineRegistrar();
        var cursor = new CountingSystem();
        var mapping = new CountingSystem();
        registrar.AddGroup("input", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("cursor", cursor);
            g.Add("mapping", mapping);
        }, PipelineCompositeKind.Parallel, runner);
        var pipeline = registrar.Build();

        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(1, cursor.UpdateCount);
        Assert.Equal(1, mapping.UpdateCount);
    }

    [Fact]
    public void Group_ParallelKind_WithoutARunner_ThrowsLoudly()
    {
        var registrar = new EditorPipelineRegistrar();
        Assert.Throws<ArgumentNullException>(() =>
            registrar.AddGroup("input", EditTimeBehavior.RunNormally,
                g => g.Add("cursor", new CountingSystem()), PipelineCompositeKind.Parallel));
    }

    [Fact]
    public void Group_WithNoChildren_ThrowsLoudly()
    {
        var registrar = new EditorPipelineRegistrar();
        Assert.Throws<InvalidOperationException>(() =>
            registrar.AddGroup("logic", EditTimeBehavior.Freeze, _ => { }));
    }

    [Fact]
    public void NestedGroups_PrefixNames_TrackDepth_AndFlattenPreOrder()
    {
        var registrar = new EditorPipelineRegistrar();
        registrar.Add("input", new CountingSystem(), EditTimeBehavior.RunNormally);
        registrar.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("movement", new CountingSystem());
            g.AddGroup("dialogue", EditTimeBehavior.RunNormally, gg =>
            {
                gg.Add("runner", new CountingSystem());
            });
        });

        // Flattened enumeration is pre-order (a group precedes its children), names are
        // hierarchical, and Depth reflects nesting for the panel's indentation.
        Assert.Equal(
            new[] { "input", "logic", "logic.movement", "logic.dialogue", "logic.dialogue.runner" },
            registrar.Entries.Select(e => e.Name));
        Assert.Equal(new[] { 0, 0, 1, 1, 2 }, registrar.Entries.Select(e => e.Depth));
        Assert.Equal(new[] { "input", "logic" }, registrar.Roots.Select(e => e.Name));

        var dialogue = registrar.GetEntry("logic.dialogue");
        Assert.True(dialogue.IsGroup);
        Assert.Same(registrar.GetEntry("logic"), dialogue.Parent);
        Assert.Equal("dialogue", dialogue.LocalName);
    }

    [Fact]
    public void Group_DuplicateChildName_Throws_SameLocalNameInAnotherGroupIsFine()
    {
        var registrar = new EditorPipelineRegistrar();
        Assert.Throws<ArgumentException>(() => registrar.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("movement", new CountingSystem());
            g.Add("movement", new CountingSystem());
        }));

        // Full names are the namespace: the same local name under a different group is distinct.
        var registrar2 = new EditorPipelineRegistrar();
        registrar2.AddGroup("logic", EditTimeBehavior.Freeze, g => g.Add("prep", new CountingSystem()));
        registrar2.AddGroup("draw", EditTimeBehavior.RunNormally, g => g.Add("prep", new CountingSystem()));
        Assert.NotNull(registrar2.GetEntry("logic.prep"));
        Assert.NotNull(registrar2.GetEntry("draw.prep"));
    }

    // ---- Enabled semantics over the tree: leaf toggles, group cascade, derived tri-state ----

    [Fact]
    public void LeafToggle_InsideAGroup_StopsExactlyThatSystem()
    {
        var registrar = new EditorPipelineRegistrar();
        var a = new CountingSystem();
        var b = new CountingSystem();
        registrar.AddGroup("logic", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("a", a);
            g.Add("b", b);
        });
        var pipeline = registrar.Build();

        registrar.SetEnabled("logic.a", false);
        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(0, a.UpdateCount); // exactly the toggled leaf stops
        Assert.Equal(1, b.UpdateCount); // its sibling keeps running
    }

    [Fact]
    public void GroupToggle_CascadesToAllDescendantLeaves()
    {
        var registrar = new EditorPipelineRegistrar();
        var a = new CountingSystem();
        var nested = new CountingSystem();
        registrar.AddGroup("logic", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("a", a);
            g.AddGroup("dialogue", EditTimeBehavior.RunNormally, gg => gg.Add("runner", nested));
        });
        var pipeline = registrar.Build();

        registrar.SetEnabled("logic", false);
        Assert.False(registrar.IsEnabled("logic.a"));
        Assert.False(registrar.IsEnabled("logic.dialogue.runner"));
        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(0, a.UpdateCount);
        Assert.Equal(0, nested.UpdateCount);

        // Re-enabling the group cascades back on.
        registrar.SetEnabled("logic", true);
        pipeline.Update(NewState(RunMode.Play));
        Assert.Equal(1, a.UpdateCount);
        Assert.Equal(1, nested.UpdateCount);
    }

    [Fact]
    public void GroupEnabledState_DerivesTriStateFromDescendantLeaves()
    {
        var registrar = new EditorPipelineRegistrar();
        registrar.AddGroup("logic", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("a", new CountingSystem());
            g.AddGroup("dialogue", EditTimeBehavior.RunNormally, gg => gg.Add("runner", new CountingSystem()));
        });

        // All leaves enabled → On.
        Assert.Equal(PipelineEnabledState.On, registrar.GetEnabledState("logic"));

        // One leaf off → Mixed, bubbling through nesting levels.
        registrar.SetEnabled("logic.dialogue.runner", false);
        Assert.Equal(PipelineEnabledState.Off, registrar.GetEnabledState("logic.dialogue"));
        Assert.Equal(PipelineEnabledState.Mixed, registrar.GetEnabledState("logic"));

        // Every leaf off → Off.
        registrar.SetEnabled("logic.a", false);
        Assert.Equal(PipelineEnabledState.Off, registrar.GetEnabledState("logic"));

        // A leaf's state is its own toggle, two-valued.
        Assert.Equal(PipelineEnabledState.Off, registrar.GetEnabledState("logic.a"));
        registrar.SetEnabled("logic.a", true);
        Assert.Equal(PipelineEnabledState.On, registrar.GetEnabledState("logic.a"));
        Assert.Equal(PipelineEnabledState.Mixed, registrar.GetEnabledState("logic"));
    }

    [Fact]
    public void GroupGate_EnforcesPolicyOnly_ItsOwnIsEnabledIsNotTheToggleAxis()
    {
        // The enabled axis lives on the LEAVES (the group's checkbox state is derived), so a
        // group cascade must never flip the group gate itself — otherwise the derived state
        // could say "all enabled" while the gate silently blocked everything.
        var registrar = new EditorPipelineRegistrar();
        var group = registrar.AddGroup("logic", EditTimeBehavior.Freeze,
            g => g.Add("a", new CountingSystem()));

        registrar.SetEnabled("logic", false);
        Assert.True(group.Gate.IsEnabled);
        Assert.Equal(PipelineEnabledState.Off, group.EnabledState);

        registrar.SetEnabled("logic", true);
        Assert.True(group.Gate.IsEnabled);
        Assert.Equal(PipelineEnabledState.On, group.EnabledState);
    }
}
