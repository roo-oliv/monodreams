using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the level-editor premises "Bounded undo with drag-coalescing" (<c>UndoBoundedCapTest</c>,
/// <c>DragCoalescingTest</c>) and "Editor-overlay entities are standalone; delete snapshots the
/// sub-graph" — the delete half (<c>DeleteUndoSnapshotTest</c>). Pure logic: an in-memory world,
/// a counter-mutating test command, and a real <see cref="DeleteEntityCommand"/> over the engine
/// serializer registry (no GraphicsDevice).
///
/// <para>Also covers <see cref="EditorHistory.PushApplied"/> — the record-without-applying entry point
/// the transport's Restart pushes its undo command through ("Restart is one-level undoable").</para>
/// </summary>
public class UndoTests
{
    /// <summary>A trivial reversible command that mutates a shared counter — DATA + apply/revert,
    /// the minimal shape <see cref="IEditorCommand"/> describes.</summary>
    private sealed class IncrementCommand : IEditorCommand
    {
        private readonly int[] _box;
        private readonly int _delta;
        public IncrementCommand(int[] box, int delta) { _box = box; _delta = delta; }
        public void Apply(World world) => _box[0] += _delta;
        public void Revert(World world) => _box[0] -= _delta;
    }

    /// <summary>A probe that only COUNTS its apply/revert calls — it can prove whether the history
    /// invoked <see cref="IEditorCommand.Apply"/> when it recorded the command.</summary>
    private sealed class CountingCommand : IEditorCommand
    {
        public int Applies;
        public int Reverts;
        public void Apply(World world) => Applies++;
        public void Revert(World world) => Reverts++;
    }

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    // ---- UndoBoundedCapTest: push cap+2 → history holds exactly cap, oldest evicted, empty-stack no-op ----

    [Fact]
    public void UndoBoundedCapTest()
    {
        using var world = new World();
        const int cap = 3;
        var history = new EditorHistory(world, capacity: cap);
        var box = new[] { 0 };

        // Push cap+2 = 5 increments of +1. Each applies immediately (counter = 5).
        for (var i = 0; i < cap + 2; i++)
            history.Push(new IncrementCommand(box, 1));

        Assert.Equal(5, box[0]);
        Assert.Equal(cap, history.Count); // history holds exactly the cap (oldest 2 evicted FIFO)

        // Undo stops at the oldest RETAINED entry: only cap undos take effect, then it is a no-op.
        for (var i = 0; i < cap; i++)
            history.Undo();
        Assert.Equal(2, box[0]); // 5 - 3 undone = 2 (the 2 evicted entries can't be undone)
        Assert.False(history.CanUndo);

        // Empty-stack undo is a no-op (no exception, no further mutation).
        history.Undo();
        history.Undo();
        Assert.Equal(2, box[0]);

        // Empty-stack redo of nothing-yet-undone-beyond-cap is also a safe no-op.
        // (Redo IS available here — we undid cap entries — so redo them all back, then over-redo.)
        for (var i = 0; i < cap; i++)
            history.Redo();
        Assert.Equal(5, box[0]);
        Assert.False(history.CanRedo);
        history.Redo(); // empty-stack redo no-op
        Assert.Equal(5, box[0]);
    }

    [Fact]
    public void Undo_EmptyHistory_IsNoOp()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        // No pushes — undo/redo must not throw and must not change anything.
        history.Undo();
        history.Redo();
        Assert.Equal(0, history.Count);
        Assert.Equal(0, history.RedoCount);
    }

    // ---- DragCoalescingTest: a coalesced transaction commits exactly ONE entry for many pushes ----

    [Fact]
    public void DragCoalescingTest()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var box = new[] { 0 };

        // Simulate a gizmo drag: begin a transaction, push many incremental edits, commit once.
        history.BeginTransaction();
        Assert.True(history.InTransaction);
        for (var i = 0; i < 10; i++)
            history.Push(new IncrementCommand(box, 1)); // each applies live (counter climbs to 10)
        Assert.Equal(10, box[0]);
        Assert.Equal(0, history.Count); // nothing recorded mid-transaction
        history.CommitTransaction();

        // The whole drag is exactly ONE undo step.
        Assert.Equal(1, history.Count);

        // One undo reverts the WHOLE drag (all 10 increments), not just the last.
        history.Undo();
        Assert.Equal(0, box[0]);

        // One redo re-applies the whole drag.
        history.Redo();
        Assert.Equal(10, box[0]);
    }

    [Fact]
    public void DragCoalescing_EmptyTransaction_RecordsNothing()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        history.BeginTransaction();
        history.CommitTransaction();
        Assert.Equal(0, history.Count); // an empty drag adds no entry
    }

    [Fact]
    public void DragCoalescing_Cancel_RevertsAndRecordsNothing()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var box = new[] { 0 };
        history.BeginTransaction();
        history.Push(new IncrementCommand(box, 5));
        Assert.Equal(5, box[0]);
        history.CancelTransaction(); // an aborted drag undoes its live effect and records nothing
        Assert.Equal(0, box[0]);
        Assert.Equal(0, history.Count);
    }

    // ---- PushApplied: record a mutation that ALREADY happened, without re-applying it ----

    [Fact]
    public void PushApplied_RecordsWithoutApplying_ThenUndoRedoDriveTheCommandNormally()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var probe = new CountingCommand();

        // The transport-Restart shape: the mutation already ran outside the history, so RECORDING it
        // must not invoke Apply (re-running the teardown just to record it would double the work).
        history.PushApplied(probe);
        Assert.Equal(0, probe.Applies);
        Assert.Equal(0, probe.Reverts);
        Assert.Equal(1, history.Count);
        Assert.True(history.CanUndo);

        // From here it is an ordinary entry: undo reverts once, redo applies once (the replayability
        // contract PushApplied relies on).
        history.Undo();
        Assert.Equal(1, probe.Reverts);
        Assert.Equal(0, probe.Applies);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Equal(1, probe.Applies);
        Assert.Equal(1, probe.Reverts);
    }

    [Fact]
    public void PushApplied_InsideAnOpenTransaction_Throws()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var probe = new CountingCommand();

        // An already-applied command cannot coalesce with a transaction's live pushes — refused loudly
        // rather than silently folded into the drag's single entry.
        history.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => history.PushApplied(probe));
        Assert.Equal(0, probe.Applies);
        Assert.Equal(0, history.Count);

        history.CancelTransaction();
        Assert.False(history.InTransaction);
    }

    // ---- DeleteUndoSnapshotTest: delete an entity with a ChildOf child → undo restores both + components ----

    [Fact]
    public void DeleteUndoSnapshotTest()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        var serializer = new SceneSerializer(registry);
        var history = new EditorHistory(world);

        // A tagged save-root with a ChildOf child (the sub-graph delete must snapshot whole).
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Player", "Hero"));
        root.Set(new TransformComponent(new Vector2(12, 34), rotation: 0.5f, scale: new Vector2(2, 2)));

        var child = world.CreateEntity();
        child.Set(new EntityInfoComponent("Orb", "BlueOrb"));
        child.Set(new TransformComponent(new Vector2(50, 0)));
        child.SetParent(root);

        Assert.Equal(2, CountWith<TransformComponent>(world));

        // Delete via the command (snapshots the sub-graph at construction, then disposes on Apply).
        history.Push(new DeleteEntityCommand(world, root, serializer));
        Assert.Equal(0, CountWith<TransformComponent>(world)); // root + child both gone
        Assert.False(root.IsAlive);
        Assert.False(child.IsAlive);

        // Undo → both restored from the component snapshot, with their components + parent graph.
        history.Undo();
        Assert.Equal(2, CountWith<TransformComponent>(world));

        var restored = EntitiesWith<TransformComponent>(world);
        var restoredRoot = restored.Single(e => e.Has<SceneObjectComponent>());
        var restoredChild = restored.Single(e => e.Has<ChildOfComponent>());

        // Root's components reproduced (Transform + EntityInfo + the re-applied SceneObject tag).
        var t = restoredRoot.Get<TransformComponent>();
        Assert.Equal(new Vector2(12, 34), t.Position);
        Assert.Equal(0.5f, t.Rotation);
        Assert.Equal(new Vector2(2, 2), t.Scale);
        Assert.Equal("Player", restoredRoot.Get<EntityInfoComponent>().Type);
        Assert.True(restoredRoot.Has<SceneObjectComponent>());

        // Parent graph reproduced: the child points back at the restored root.
        Assert.Equal(restoredRoot, restoredChild.Get<ChildOfComponent>().Parent);
        Assert.Equal(new Vector2(50, 0), restoredChild.Get<TransformComponent>().Position);

        // Redo deletes again (deterministic).
        history.Redo();
        Assert.Equal(0, CountWith<TransformComponent>(world));
    }

    private static int CountWith<T>(World world)
    {
        using var set = world.GetEntities().With<T>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
    }

    private static List<Entity> EntitiesWith<T>(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities()) list.Add(e);
        return list;
    }
}
