#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The applying system for the editor command/undo machinery: in <see cref="RunMode.Edit"/> it
/// translates the designer's intent (delete the selection / undo / redo, expressed as predicates the
/// editor screen wires to input edges) into operations on the <see cref="EditorHistory"/>. ECS purity:
/// the commands are data + apply/revert (see <see cref="IEditorCommand"/>); this system only sequences
/// them — it holds no mutation logic of its own.
///
/// <para>Edit-guarded, registered <see cref="MonoDreams.System.EditTimeBehavior.RunNormally"/>: inert
/// in Play, active in Edit. Delete builds a <see cref="DeleteEntityCommand"/> for the selected entity (snapshotting its
/// sub-graph for undo) and pushes it; undo/redo drive the bounded history (empty-stack = no-op).</para>
///
/// <para>The transform-edit and create commands are pushed by other paths — the gizmo (Wave 4b) via
/// the history's coalescing API, and a placement path via <see cref="CreateEntityCommand"/> — so this
/// system deliberately covers only the keyboard-driven delete/undo/redo for Wave 4a.</para>
/// </summary>
public sealed class EditorCommandSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EditorHistory _history;
    private readonly SceneSerializer _serializer;
    private readonly EntitySet _selectedSet;
    private readonly Func<GameState, bool> _deleteRequested;
    private readonly Func<GameState, bool> _undoRequested;
    private readonly Func<GameState, bool> _redoRequested;

    public bool IsEnabled { get; set; } = true;

    public EditorCommandSystem(
        World world,
        EditorHistory history,
        SceneSerializer serializer,
        Func<GameState, bool> deleteRequested,
        Func<GameState, bool> undoRequested,
        Func<GameState, bool> redoRequested)
    {
        _world = world;
        _history = history;
        _serializer = serializer;
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _deleteRequested = deleteRequested;
        _undoRequested = undoRequested;
        _redoRequested = redoRequested;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        if (state.RunMode != RunMode.Edit) return; // Edit-guarded: inert in Play

        if (_undoRequested(state)) _history.Undo();
        if (_redoRequested(state)) _history.Redo();

        if (_deleteRequested(state))
        {
            Entity? selected = null;
            foreach (var e in _selectedSet.GetEntities()) { selected = e; break; }
            if (selected is { } target && target.IsAlive)
                _history.Push(new DeleteEntityCommand(_world, target, _serializer));
        }
    }

    public void Dispose()
    {
        _selectedSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
