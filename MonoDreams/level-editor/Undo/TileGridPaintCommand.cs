#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component.Level;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// One paint edit of a <see cref="TileGridComponent"/>'s cells: the affected cells with their
/// before/after value ids (0 = empty — the entry is removed). A brush stroke pushes one command
/// per newly painted cell inside a coalescing history transaction (the gizmo-drag pattern), so the
/// whole stroke undoes as ONE step. Apply/Revert both publish <c>NotifyChanged</c> so the bake
/// system re-derives tiles + colliders after an undo/redo exactly as after a live paint.
/// </summary>
public sealed class TileGridPaintCommand : IEditorCommand
{
    private readonly Entity _grid;
    private readonly long _cell;
    private readonly byte _before;
    private readonly byte _after;

    public TileGridPaintCommand(Entity grid, long cell, byte before, byte after)
    {
        _grid = grid;
        _cell = cell;
        _before = before;
        _after = after;
    }

    public void Apply(World world) => Write(_after);

    public void Revert(World world) => Write(_before);

    private void Write(byte value)
    {
        if (!_grid.IsAlive || !_grid.Has<TileGridComponent>()) return;
        var cells = _grid.Get<TileGridComponent>().Cells;
        if (value == 0) cells.Remove(_cell);
        else cells[_cell] = value;
        _grid.NotifyChanged<TileGridComponent>();
    }
}
