#nullable enable
using System.Collections.Generic;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Bundles several <see cref="IEditorCommand"/>s into one undo/redo step. The history collapses a
/// coalesced transaction (e.g. one gizmo drag = many incremental transform edits) into a single
/// composite entry so the whole drag undoes/redoes as one. <see cref="Apply"/> replays the children
/// in order; <see cref="Revert"/> reverts them in reverse order (so nested mutations unwind cleanly).
/// </summary>
public sealed class CompositeCommand : IEditorCommand
{
    private readonly List<IEditorCommand> _children;

    public CompositeCommand(List<IEditorCommand> children) => _children = children;

    /// <summary>The bundled child commands (in apply order).</summary>
    public IReadOnlyList<IEditorCommand> Children => _children;

    public void Apply(World world)
    {
        for (var i = 0; i < _children.Count; i++)
            _children[i].Apply(world);
    }

    public void Revert(World world)
    {
        for (var i = _children.Count - 1; i >= 0; i--)
            _children[i].Revert(world);
    }
}
