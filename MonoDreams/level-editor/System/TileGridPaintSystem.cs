#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The tile-grid paint brush (the <see cref="EditorToolMode.GroundPaint"/> owner): while the
/// palette's Paint tab has a <c>TilePaintValue</c> armed (<see cref="GizmoStateComponent.PaintValue"/>;
/// 0 = eraser), holding the left button paints the cell under the cursor — a drag paints every cell
/// crossed, each edit one <see cref="TileGridPaintCommand"/> pushed into a coalescing history
/// transaction, so the whole stroke is ONE undo step (the gizmo-drag pattern). Every applied edit
/// publishes <c>NotifyChanged</c>; the bake system re-derives tiles + colliders after its quiet
/// window, while the paint-view overlay shows the logical cells live.
///
/// <para><b>The grid the brush targets.</b> The brush paints into the ACTIVE Paint layer's
/// <see cref="TileGridComponent"/> (a Paint layer IS its grid — the layers model), so arming is
/// layer-driven and the target is never ambiguous. A single-grid LEGACY scene (a bare grid entity
/// predating the layers model) is upgraded into a real Paint layer on the first paint and activated,
/// so old scenes join the model without a migration. With no Paint layer the brush refuses loud
/// (pointing at the Entities panel's "New Indexed Layer"), a LOCKED layer refuses loud (the Aseprite
/// rule the palette's placement follows), and painting inside a prefab context refuses loud too (a
/// prefab is a class; the scene's terrain grid is scene content).</para>
/// </summary>
public sealed class TileGridPaintSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EditorHistory _history;
    private readonly EditorShellStateComponent? _shellState;
    private readonly Action<string>? _notifyWarning;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _gizmoStateSet;
    private readonly EntitySet _grids;

    private bool _stroking;
    private bool _refusalLogged; // one log line per stroke — a held drag must not spam the log
    private long? _lastPaintedCell;
    private (int X, int Y)? _lastCell; // the previous frame's cursor cell — fast drags interpolate

    public bool IsEnabled { get; set; } = true;

    public TileGridPaintSystem(World world, EditorHistory history,
        EditorShellStateComponent? shellState = null,
        Action<string>? notifyWarning = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _shellState = shellState;
        _notifyWarning = notifyWarning;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
        _grids = world.GetEntities().With<TileGridComponent>().With<TransformComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        if (state.RunMode != RunMode.Edit)
        {
            EndStroke();
            return;
        }

        var gizmo = ReadGizmoState();
        if (gizmo.Mode != EditorToolMode.GroundPaint)
        {
            EndStroke();
            return;
        }

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();

            if (input.LeftButtonPressed && !input.OutsideViewport)
            {
                _history.BeginTransaction();
                _stroking = true;
                _refusalLogged = false;
                _lastPaintedCell = null;
                _lastCell = null;
            }

            if (_stroking && input.LeftButton && !input.OutsideViewport)
                PaintAt(input.WorldPosition, gizmo.PaintValue);

            if (_stroking && (input.LeftButtonReleased || !input.LeftButton))
                EndStroke();

            return; // single cursor
        }
    }

    private void PaintAt(Vector2 worldPosition, byte value)
    {
        var grid = FindOrCreateGrid(value);
        if (grid == null) return;

        var data = grid.Value.Get<TileGridComponent>();
        var anchor = grid.Value.Get<TransformComponent>().WorldPosition;
        var (x, y) = data.CellAt(worldPosition, anchor);

        // A fast drag can jump more than one cell between frames — walk the line from the previous
        // cursor cell so the stroke never leaves holes (the brush paints a continuous path).
        if (_lastCell is { } from && (Math.Abs(from.X - x) > 1 || Math.Abs(from.Y - y) > 1))
        {
            var steps = Math.Max(Math.Abs(from.X - x), Math.Abs(from.Y - y));
            for (var i = 1; i < steps; i++)
            {
                var ix = from.X + (int)Math.Round((x - from.X) * (i / (float)steps));
                var iy = from.Y + (int)Math.Round((y - from.Y) * (i / (float)steps));
                PaintCell(grid.Value, data, ix, iy, value);
            }
        }
        _lastCell = (x, y);

        PaintCell(grid.Value, data, x, y, value);
    }

    private void PaintCell(Entity grid, TileGridComponent data, int x, int y, byte value)
    {
        var key = TileGridComponent.Pack(x, y);
        if (_lastPaintedCell == key) return; // still in the same cell
        _lastPaintedCell = key;

        var before = data.Cells.TryGetValue(key, out var current) ? current : (byte)0;
        if (before == value) return; // already that value — nothing to record

        _history.Push(new TileGridPaintCommand(grid, key, before, value));
    }

    /// <summary>The grid the brush paints into — the ACTIVE layer's <see cref="TileGridComponent"/>
    /// (layers wave: a Paint layer IS its grid). Loud null when the active layer is not a Paint
    /// layer (arming is layer-driven, so this is a self-heal path, not the normal flow) or when the
    /// target layer is LOCKED (the placement rule: a locked layer refuses the edit, loud).</summary>
    private Entity? FindOrCreateGrid(byte value)
    {
        if (PrefabContextRoot.ResolveIfPrefab(_world, _shellState).IsAlive)
        {
            _notifyWarning?.Invoke("Painting is scene-only - a prefab has no terrain grid");
            return null;
        }

        var active = _shellState?.ActiveLayer ?? default;
        if (active.IsAlive && active.Has<TileGridComponent>())
            return RefuseIfLocked(active);

        // Fallback: exactly one paint grid in the scene — paint into it (and activate it). A
        // LEGACY grid entity (pre-layers "PaintGrid") is upgraded into a real Paint layer here,
        // so old scenes join the layers model the first time they are painted.
        Entity only = default;
        var count = 0;
        foreach (var grid in _grids.GetEntities()) { only = grid; count++; }
        if (count == 1)
        {
            if (RefuseIfLocked(only) == null) return null;
            if (!only.Has<SceneLayerComponent>())
            {
                only.Set(new SceneLayerComponent { Order = 0 });
                Logger.Info("[level-editor] Upgraded the legacy paint grid into a Paint layer.");
            }
            if (_shellState != null) _shellState.ActiveLayer = only;
            return only;
        }

        _notifyWarning?.Invoke("Select a Paint layer to paint into (Entities panel - New Indexed Layer)");
        return null;
    }

    /// <summary>Passes <paramref name="layer"/> through, or refuses it LOUD (null) when it carries a
    /// locked <c>SceneLayerComponent</c> — the same Aseprite rule the palette's placement follows
    /// (a locked layer is not an edit target, and a silent no-op would read as a broken brush).</summary>
    private Entity? RefuseIfLocked(Entity layer)
    {
        if (!layer.Has<SceneLayerComponent>() || !layer.Get<SceneLayerComponent>().Locked) return layer;
        var name = MonoDreams.System.Level.SceneLayerSystem.LayerName(layer);
        _notifyWarning?.Invoke($"Layer '{name}' is locked - unlock it to paint");
        if (!_refusalLogged)
        {
            _refusalLogged = true;
            Logger.Warning($"[level-editor] Paint refused: the layer '{name}' is LOCKED.");
        }
        return null;
    }

    private void EndStroke()
    {
        if (!_stroking) return;
        _stroking = false;
        _lastPaintedCell = null;
        _lastCell = null;
        if (_history.InTransaction) _history.CommitTransaction();
    }

    private GizmoStateComponent ReadGizmoState()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return e.Get<GizmoStateComponent>();
        return GizmoStateComponent.Default;
    }

    public void Dispose()
    {
        EndStroke();
        _cursorSet.Dispose();
        _gizmoStateSet.Dispose();
        _grids.Dispose();
    }
}
