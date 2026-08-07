#nullable enable
using DefaultEcs;
using MonoDreams.Component.Level;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Adds a NEW <see cref="TilePaintValue"/> (a new paintable index) to a Paint layer's grid — the
/// "+ New" affordance in the shelf's Paint view. Undo removes it again by id. History is LIFO, so
/// cells painted with the new id are always un-painted before the creation itself un-does — an
/// orphaned cell id can't arise from ordinary undo flow.
/// </summary>
public sealed class AddPaintValueCommand : IEditorCommand
{
    private readonly Entity _grid;
    private readonly TilePaintValue _value;

    public AddPaintValueCommand(Entity grid, TilePaintValue value)
    {
        _grid = grid;
        _value = value;
    }

    public void Apply(World world)
    {
        if (!_grid.IsAlive || !_grid.Has<TileGridComponent>()) return;
        var grid = _grid.Get<TileGridComponent>();
        if (grid.FindValue(_value.Id) == null) grid.Values.Add(_value);
        _grid.NotifyChanged<TileGridComponent>();
    }

    public void Revert(World world)
    {
        if (!_grid.IsAlive || !_grid.Has<TileGridComponent>()) return;
        var grid = _grid.Get<TileGridComponent>();
        grid.Values.RemoveAll(v => v.Id == _value.Id);
        _grid.NotifyChanged<TileGridComponent>();
    }
}
