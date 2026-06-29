#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Bounded undo/redo history for editor commands. Holds at most <see cref="Capacity"/> entries;
/// when a push would exceed the cap the <b>oldest</b> entry is evicted (FIFO) — an old edit simply
/// drops off the bottom rather than blocking the new one. <c>Undo</c> on an empty undo stack and
/// <c>Redo</c> on an empty redo stack are <b>no-ops</b> (no exception), so the toolbar can wire the
/// buttons unconditionally.
///
/// <para><b>Drag-coalescing.</b> <see cref="BeginTransaction"/> opens a coalesced transaction: while
/// open, <see cref="Push"/>ed commands are applied immediately (the live edit shows on screen) and
/// accumulated, but no history entry is added. <see cref="CommitTransaction"/> collapses the whole
/// accumulation into a single <see cref="CompositeCommand"/> entry — so one full gizmo drag becomes
/// exactly one undo step. An empty transaction commits nothing. <see cref="CancelTransaction"/>
/// reverts and discards the accumulation (e.g. an escaped drag).</para>
///
/// <para>The history is the <b>data store</b>; an applying system / the toolbar drives it. It holds
/// no per-frame state and allocates only on push/transaction boundaries (not in any hot path).</para>
/// </summary>
public sealed class EditorHistory
{
    /// <summary>Default cap when none is supplied — generous for hand-scale editing, still bounded.</summary>
    public const int DefaultCapacity = 100;

    private readonly World _world;
    private readonly LinkedList<IEditorCommand> _undo = new(); // First = oldest, Last = newest.
    private readonly Stack<IEditorCommand> _redo = new();

    private List<IEditorCommand>? _transaction;

    public EditorHistory(World world, int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be >= 1.");
        _world = world;
        Capacity = capacity;
    }

    /// <summary>Maximum retained undo entries; the oldest is evicted FIFO past this.</summary>
    public int Capacity { get; }

    /// <summary>Current number of undoable entries (≤ <see cref="Capacity"/>).</summary>
    public int Count => _undo.Count;

    /// <summary>Number of redoable entries (entries undone but not yet superseded by a new push).</summary>
    public int RedoCount => _redo.Count;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Whether a coalescing transaction is currently open.</summary>
    public bool InTransaction => _transaction != null;

    /// <summary>
    /// Applies <paramref name="command"/> immediately and records it. Outside a transaction this is
    /// one history entry; inside an open transaction it is accumulated to collapse into one entry on
    /// commit. A normal push clears the redo stack (a new edit invalidates the redo future).
    /// </summary>
    public void Push(IEditorCommand command)
    {
        command.Apply(_world);

        if (_transaction != null)
        {
            _transaction.Add(command);
            return;
        }

        AddEntry(command);
    }

    /// <summary>Begins a coalescing transaction. Pushes until <see cref="CommitTransaction"/> apply
    /// live but collapse into one entry. Idempotent guard: throws if a transaction is already open.</summary>
    public void BeginTransaction()
    {
        if (_transaction != null)
            throw new InvalidOperationException("A coalescing transaction is already open.");
        _transaction = new List<IEditorCommand>();
    }

    /// <summary>
    /// Commits the open transaction as a single history entry (one <see cref="CompositeCommand"/> for
    /// multiple commands, or the lone command directly). An empty transaction commits nothing. The
    /// child commands were already applied during the transaction, so commit only records them.
    /// </summary>
    public void CommitTransaction()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No coalescing transaction is open.");

        var batch = _transaction;
        _transaction = null;

        if (batch.Count == 0) return; // nothing was pushed → no entry
        AddEntry(batch.Count == 1 ? batch[0] : new CompositeCommand(batch));
    }

    /// <summary>Cancels the open transaction: reverts every accumulated command (newest-first) and
    /// adds no history entry. For an aborted drag.</summary>
    public void CancelTransaction()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No coalescing transaction is open.");

        var batch = _transaction;
        _transaction = null;
        for (var i = batch.Count - 1; i >= 0; i--)
            batch[i].Revert(_world);
    }

    /// <summary>Reverts the most recent entry, moving it to the redo stack. No-op on an empty stack.</summary>
    public void Undo()
    {
        if (_undo.Count == 0) return; // empty-stack no-op (no exception)
        var command = _undo.Last!.Value;
        _undo.RemoveLast();
        command.Revert(_world);
        _redo.Push(command);
    }

    /// <summary>Re-applies the most recently undone entry, moving it back to the undo stack. No-op on
    /// an empty redo stack.</summary>
    public void Redo()
    {
        if (_redo.Count == 0) return; // empty-stack no-op (no exception)
        var command = _redo.Pop();
        command.Apply(_world);
        _undo.AddLast(command);
    }

    private void AddEntry(IEditorCommand command)
    {
        _undo.AddLast(command);
        if (_undo.Count > Capacity)
            _undo.RemoveFirst(); // evict the oldest (FIFO)
        _redo.Clear(); // a fresh edit invalidates the redo future
    }
}
