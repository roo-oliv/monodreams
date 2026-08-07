#nullable enable
using DefaultEcs;
using MonoDreams.Component.Level;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// One edit of a <see cref="TilePaintValue"/>'s VISUAL definition — its autotile rules and/or its
/// tileset binding (key + tile size) — as before/after snapshots keyed by the value's stable
/// <see cref="TilePaintValue.Id"/> (indices shift when values are added/removed; ids never do).
/// The Autotile Rules workspace pushes one of these per edit (a case's tile toggle, a tileset
/// pick), so rules editing is LIVE — the world re-skins immediately (Apply/Revert publish
/// <c>NotifyChanged</c>, the bake's trigger) — and every step undoes through the ONE shared history
/// like any other scene edit. No Save/Cancel layer: the history IS the safety net.
/// </summary>
public sealed class PaintValueEditCommand : IEditorCommand
{
    private readonly Entity _grid;
    private readonly byte _valueId;
    private readonly string? _rulesBefore, _rulesAfter;
    private readonly string? _tilesetBefore, _tilesetAfter;
    private readonly int _tileSizeBefore, _tileSizeAfter;

    private PaintValueEditCommand(Entity grid, byte valueId,
        string? rulesBefore, string? rulesAfter,
        string? tilesetBefore, string? tilesetAfter,
        int tileSizeBefore, int tileSizeAfter)
    {
        _grid = grid;
        _valueId = valueId;
        _rulesBefore = rulesBefore;
        _rulesAfter = rulesAfter;
        _tilesetBefore = tilesetBefore;
        _tilesetAfter = tilesetAfter;
        _tileSizeBefore = tileSizeBefore;
        _tileSizeAfter = tileSizeAfter;
    }

    /// <summary>An autotile-rules edit (tileset unchanged).</summary>
    public static PaintValueEditCommand Rules(Entity grid, TilePaintValue value, string? newRules) =>
        new(grid, value.Id, value.AutotileRules, newRules,
            value.TilesetKey, value.TilesetKey, value.TileSize, value.TileSize);

    /// <summary>A tileset re-bind (rules unchanged — the mapping usually still applies; masks are
    /// sheet-agnostic and cell indices are the designer's to fix up next).</summary>
    public static PaintValueEditCommand Tileset(Entity grid, TilePaintValue value, string? newKey, int newTileSize) =>
        new(grid, value.Id, value.AutotileRules, value.AutotileRules,
            value.TilesetKey, newKey, value.TileSize, newTileSize);

    public void Apply(World world) => Write(_rulesAfter, _tilesetAfter, _tileSizeAfter);

    public void Revert(World world) => Write(_rulesBefore, _tilesetBefore, _tileSizeBefore);

    private void Write(string? rules, string? tileset, int tileSize)
    {
        if (!_grid.IsAlive || !_grid.Has<TileGridComponent>()) return;
        var value = _grid.Get<TileGridComponent>().FindValue(_valueId);
        if (value == null) return;
        value.AutotileRules = rules;
        value.TilesetKey = tileset;
        value.TileSize = tileSize;
        _grid.NotifyChanged<TileGridComponent>(); // the bake re-skins painted cells next frame
    }
}

/// <summary>
/// Adds a NEW <see cref="TilePaintValue"/> (a new paintable index / rule set) to a Paint layer's
/// grid — the "+ New Index" affordance in the shelf and the Autotile Rules workspace. Undo removes
/// it again by id. History is LIFO, so cells painted with the new id are always un-painted before
/// the creation itself un-does — an orphaned cell id can't arise from ordinary undo flow.
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
