using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the editor-screen run-state policy that the §4 interaction matrix fixes (and the
/// foundation premise "Edit-time behavior is a per-system policy honored by GatedSystem"): while
/// the transport is Paused (Edit), a <c>Freeze</c>-wrapped system skips while <c>HierarchySystem</c>
/// (RunNormally) keeps propagating transform edits, and the transport's Play/Pause is an in-place
/// <c>RunMode</c> flip — no screen swap, world state preserved.
///
/// Named tests: <c>HierarchyLiveInEditTest</c>, <c>RunStateGatingInEditorScreenTest</c>.
/// Pure logic: a real world + the real <c>HierarchySystem</c> / <c>GatedSystem</c> /
/// <c>EditorTransport</c>; no GraphicsDevice.
/// </summary>
public class EditorRunStateTests
{
    private sealed class CountingSystem : ISystem<GameState>
    {
        public int Runs;
        public bool IsEnabled { get; set; } = true;
        public void Update(GameState state) => Runs++;
        public void Dispose() { }
    }

    // ---- HierarchyLiveInEditTest: in Edit, edit a parent transform → child WorldPosition updates ----

    [Fact]
    public void HierarchyLiveInEditTest()
    {
        using var world = new World();

        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(new Vector2(100, 100)));

        var child = world.CreateEntity();
        child.Set(new TransformComponent(new Vector2(10, 0))); // local offset from parent
        child.SetParent(parent);

        using var hierarchy = new HierarchySystem(world);
        var editState = new GameState(new GameTime()) { RunMode = RunMode.Edit };

        // First frame in Edit: hierarchy syncs the parent link and the child's world position.
        hierarchy.Update(editState);
        Assert.Equal(new Vector2(110, 100), child.Get<TransformComponent>().WorldPosition);

        // The designer edits the PARENT transform while the transport is Paused.
        parent.Get<TransformComponent>().Position = new Vector2(200, 300);

        // HierarchySystem is RunNormally (NOT frozen) in Edit, so the edit propagates to the child
        // the same frame: the child's world position follows the parent.
        hierarchy.Update(editState);
        Assert.Equal(new Vector2(210, 300), child.Get<TransformComponent>().WorldPosition);
    }

    // ---- RunStateGatingInEditorScreenTest: Freeze skips in Edit, RunNormally runs; the transport flips in place ----

    [Fact]
    public void RunStateGatingInEditorScreenTest()
    {
        using var world = new World();

        var frozen = new CountingSystem();   // stands in for physics/camera-follow (Freeze in Edit)
        var live = new CountingSystem();      // stands in for render/cursor (RunNormally in both)
        var frozenGate = new GatedSystem(frozen, EditTimeBehavior.Freeze);
        var liveGate = new GatedSystem(live, EditTimeBehavior.RunNormally);

        var play = new GameState(new GameTime()) { RunMode = RunMode.Play };
        var edit = new GameState(new GameTime()) { RunMode = RunMode.Edit };

        // In Play both run.
        frozenGate.Update(play); liveGate.Update(play);
        Assert.Equal(1, frozen.Runs);
        Assert.Equal(1, live.Runs);

        // In Edit the Freeze-wrapped system skips; the RunNormally one keeps running.
        frozenGate.Update(edit); liveGate.Update(edit);
        Assert.Equal(1, frozen.Runs); // still 1 — skipped in Edit
        Assert.Equal(2, live.Runs);   // ran again

        // The transport flips RunMode IN PLACE — no screen Dispose/Load, the same GameState + world persist.
        var marker = world.CreateEntity();
        marker.Set(new TransformComponent(new Vector2(7, 7)));

        var transport = new EditorTransport(world, new EditorHistory(world));
        var state = new GameState(new GameTime()); // defaults to Play

        Assert.Equal(RunMode.Play, state.RunMode);
        transport.TogglePlayPause(state);
        Assert.Equal(RunMode.Edit, state.RunMode); // Playing → Paused, same state object
        transport.TogglePlayPause(state);
        Assert.Equal(RunMode.Play, state.RunMode); // Paused → Playing

        // World state is untouched by the toggle (no swap): the marker entity is still alive with its data.
        Assert.True(marker.IsAlive);
        Assert.Equal(new Vector2(7, 7), marker.Get<TransformComponent>().Position);
    }
}
