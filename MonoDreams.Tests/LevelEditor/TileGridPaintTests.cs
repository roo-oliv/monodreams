using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the terrain brush — <c>TileGridPaintSystem</c> + <c>TileGridPaintCommand</c> — over the
/// real system, in-process (the <c>PalettePlacementTests</c>/<c>GizmoTests</c> style: an in-memory
/// world, a scripted cursor, no GraphicsDevice and no chrome). The four claims the tool lives or
/// dies by:
///
/// <list type="bullet">
///   <item><b>Interpolated strokes:</b> a drag that jumps N cells between frames paints the whole
///   <c>max(|dx|,|dy|)</c>-step line — a hole-free 8-connected chain from the press cell to the drag
///   cell, at any speed. A dotted trail of orphan cells is the failure this exists to prevent.</item>
///   <item><b>One stroke = one undo step:</b> every cell edit is a <c>TileGridPaintCommand</c> pushed
///   into ONE coalescing transaction, committed on release; a single Undo empties the whole stroke,
///   Redo restores it, and a second stroke undoes alone.</item>
///   <item><b>Eraser round-trip:</b> paint value 0 REMOVES the dictionary entry (sparse cells, never
///   a stored zero) and undo/redo walk the erase back and forth without leaving ghosts.</item>
///   <item><b>Loud refusals:</b> a LOCKED active layer, a prefab context, and an unresolvable grid
///   each warn and paint nothing — and cost no undo step, leaving no dangling open transaction that
///   would break the next <c>BeginTransaction</c>.</item>
/// </list>
///
/// Names the level-editor premises "The Paint tab arms a tile-grid brush; the paint VIEW shows
/// logical colored blocks; strokes are one undo step" (all of the above), "Bounded undo with
/// drag-coalescing" (the transaction pattern the stroke reuses) and "Viewport presses belong to
/// exactly one tool family" (the brush acts only in <c>GroundPaint</c>); plus the level-loading
/// premise "The paint grid is authored cells + values; everything visible/collidable is a bake
/// product" (this test edits ONLY authored cells — the bake is <see cref="TileGridBakeSystemTests"/>).
/// </summary>
public class TileGridPaintTests
{
    private const float CellSize = 32f;

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    // ---- rig ---------------------------------------------------------------------------------

    /// <summary>The shared editor-state entity the brush reads its modality + armed value from
    /// (the ONE <c>GizmoStateComponent</c>, armed into <c>GroundPaint</c>).</summary>
    private static Entity MakeGizmo(World world, byte paintValue = 1)
    {
        var e = world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        var state = GizmoStateComponent.Default;
        state.Mode = EditorToolMode.GroundPaint;
        state.PaintValue = paintValue;
        e.Set(state);
        return e;
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    /// <summary>Writes one frame of cursor state. <c>Pressed</c>/<c>Released</c> are ONE-frame edges
    /// (that is how <c>CursorInputSystem</c> delivers them), so every frame rewrites the whole set.</summary>
    private static void SetCursor(Entity cursor, Vector2 world, bool leftPressed = false,
        bool leftDown = false, bool leftReleased = false, bool outsideViewport = false)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = world;
        input.VirtualPosition = world;
        input.LeftButtonPressed = leftPressed;
        input.LeftButton = (leftDown || leftPressed) && !leftReleased;
        input.LeftButtonReleased = leftReleased;
        input.OutsideViewport = outsideViewport;
    }

    /// <summary>A grid entity: a transform (the ONE anchor — cell (0,0)'s top-left sits on it) plus a
    /// <see cref="TileGridComponent"/>. <paramref name="asLayer"/> makes it a real Paint LAYER
    /// (<see cref="SceneLayerComponent"/>); leaving it off models a legacy pre-layers grid.</summary>
    private static (Entity Entity, TileGridComponent Data) MakeGrid(World world,
        Vector2 anchor = default, bool asLayer = true, bool locked = false)
    {
        var data = new TileGridComponent { CellSize = CellSize };
        data.Values.Add(new TilePaintValue { Id = 1, Name = "Dirt", Color = Color.SaddleBrown });
        var e = world.CreateEntity();
        e.Set(new TransformComponent(anchor));
        e.Set(new EntityInfoComponent("Layer", "Terrain"));
        if (asLayer) e.Set(new SceneLayerComponent { Order = 0, Locked = locked });
        e.Set(data);
        return (e, data);
    }

    /// <summary>The world point at the CENTRE of cell (x, y) for a grid anchored at the origin — the
    /// unambiguous sample point (a corner would sit on the floor() boundary).</summary>
    private static Vector2 CellCentre(int x, int y) =>
        new(x * CellSize + CellSize / 2f, y * CellSize + CellSize / 2f);

    private static Vector2 CellCentre((int X, int Y) cell) => CellCentre(cell.X, cell.Y);

    /// <summary>One complete gesture: the press frame on <paramref name="samples"/>[0], one held
    /// frame per subsequent sample (the frames a real drag delivers — the brush interpolates BETWEEN
    /// them), then the release frame. Passing two distant samples IS a fast drag.</summary>
    private static void Stroke(TileGridPaintSystem paint, Entity cursor, GameState state,
        params (int X, int Y)[] samples)
    {
        SetCursor(cursor, CellCentre(samples[0]), leftPressed: true);
        paint.Update(state);
        for (var i = 1; i < samples.Length; i++)
        {
            SetCursor(cursor, CellCentre(samples[i]), leftDown: true);
            paint.Update(state);
        }
        SetCursor(cursor, CellCentre(samples[samples.Length - 1]), leftReleased: true);
        paint.Update(state);
    }

    private static List<(int X, int Y)> PaintedCells(TileGridComponent data)
    {
        var cells = new List<(int X, int Y)>();
        foreach (var pair in data.Cells) cells.Add(TileGridComponent.Unpack(pair.Key));
        return cells;
    }

    /// <summary>
    /// The interpolation premise, stated as a PROPERTY rather than as a copy of the implementation's
    /// arithmetic: the painted cells are exactly one per step of the <c>max(|dx|,|dy|)</c> line walk
    /// (count), they include both ends, and — ordered along the dominant axis — every consecutive
    /// pair touches (Chebyshev distance 1). A hole anywhere fails the count AND the adjacency.
    /// </summary>
    private static void AssertHoleFreeStroke(TileGridComponent data, byte value,
        (int X, int Y) from, (int X, int Y) to)
    {
        var painted = PaintedCells(data);
        foreach (var pair in data.Cells) Assert.Equal(value, pair.Value);

        var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        Assert.Equal(steps + 1, painted.Count); // one cell per step: no holes, no duplicates
        Assert.Contains(from, painted);
        Assert.Contains(to, painted);

        var dominantX = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        var ascending = dominantX ? to.X >= from.X : to.Y >= from.Y;
        painted.Sort((a, b) =>
        {
            var left = dominantX ? a.X : a.Y;
            var right = dominantX ? b.X : b.Y;
            return ascending ? left.CompareTo(right) : right.CompareTo(left);
        });

        Assert.Equal(from, painted[0]);
        Assert.Equal(to, painted[painted.Count - 1]);
        for (var i = 1; i < painted.Count; i++)
        {
            var dx = Math.Abs(painted[i].X - painted[i - 1].X);
            var dy = Math.Abs(painted[i].Y - painted[i - 1].Y);
            Assert.True(dx <= 1 && dy <= 1 && (dx != 0 || dy != 0),
                $"stroke gap between {painted[i - 1]} and {painted[i]}");
        }
    }

    // ---- 1. interpolated strokes leave no gaps at ANY drag speed -----------------------------

    [Fact]
    public void FastDrag_InterpolatesTheWholeCellLine_LeavingNoHoles()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        // Two samples 10 cells apart in x and 7 in y — a real 60 fps swipe, not a per-cell walk.
        Stroke(paint, cursor, Edit(), (0, 0), (10, 7));

        AssertHoleFreeStroke(data, value: 1, from: (0, 0), to: (10, 7));
        Assert.Equal(11, data.Cells.Count); // max(10, 7) + 1
    }

    [Fact]
    public void WildDrag_PaintsTheSameContinuousLine_SoStrokesAreSpeedIndependent()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        // A far wilder jump, into negative cells: the stroke must still be one unbroken chain —
        // the tool's feel cannot depend on how fast the hand moved between two frames.
        Stroke(paint, cursor, Edit(), (0, 0), (37, -23));

        AssertHoleFreeStroke(data, value: 1, from: (0, 0), to: (37, -23));
        Assert.Equal(38, data.Cells.Count); // max(37, 23) + 1
        Assert.Equal(1, history.Count);     // still ONE undo step, however long the jump
    }

    // ---- 2. one stroke = one undo step --------------------------------------------------------

    [Fact]
    public void OneStroke_IsExactlyOneUndoStep_UndoRedoMoveTheWholeStroke()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        Stroke(paint, cursor, Edit(), (0, 0), (5, 0));

        Assert.Equal(6, data.Cells.Count);
        Assert.Equal(1, history.Count); // six cell commands coalesced into ONE entry
        Assert.False(history.InTransaction);
        Assert.True(history.CanUndo);

        history.Undo();
        Assert.Empty(data.Cells); // one Ctrl+Z, not six
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Equal(6, data.Cells.Count);
        for (var x = 0; x <= 5; x++)
            Assert.Equal((byte)1, data.Cells[TileGridComponent.Pack(x, 0)]);
    }

    [Fact]
    public void ASecondStroke_UndoesAlone_LeavingTheFirstStrokesCellsPainted()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        Stroke(paint, cursor, Edit(), (0, 0), (5, 0)); // 6 cells
        Stroke(paint, cursor, Edit(), (0, 3), (3, 3)); // 4 cells, a separate gesture

        Assert.Equal(10, data.Cells.Count);
        Assert.Equal(2, history.Count);

        history.Undo();

        Assert.Equal(6, data.Cells.Count); // only the SECOND stroke went away
        for (var x = 0; x <= 5; x++)
            Assert.True(data.Cells.ContainsKey(TileGridComponent.Pack(x, 0)));
        for (var x = 0; x <= 3; x++)
            Assert.False(data.Cells.ContainsKey(TileGridComponent.Pack(x, 3)));
    }

    // ---- 3. the eraser round-trips -------------------------------------------------------------

    [Fact]
    public void Eraser_RemovesTheEntries_AndUndoRedoRoundTripsTheWholeErase()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        var gizmo = MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        Stroke(paint, cursor, Edit(), (0, 0), (3, 0));
        Assert.Equal(4, data.Cells.Count);

        // Value 0 IS the eraser (the armed "Eraser" card).
        gizmo.Get<GizmoStateComponent>().PaintValue = 0;
        Stroke(paint, cursor, Edit(), (0, 0), (3, 0));

        // REMOVED, never stored as a zero — the cell map is sparse (the bake iterates it directly).
        Assert.Empty(data.Cells);
        for (var x = 0; x <= 3; x++)
            Assert.False(data.Cells.ContainsKey(TileGridComponent.Pack(x, 0)));
        Assert.Equal(2, history.Count); // paint stroke + erase stroke

        history.Undo(); // un-erase
        Assert.Equal(4, data.Cells.Count);
        for (var x = 0; x <= 3; x++)
            Assert.Equal((byte)1, data.Cells[TileGridComponent.Pack(x, 0)]);

        history.Undo(); // un-paint
        Assert.Empty(data.Cells);

        history.Redo(); // re-paint
        Assert.Equal(4, data.Cells.Count);

        history.Redo(); // re-erase
        Assert.Empty(data.Cells);
        Assert.Equal(2, history.Count);
    }

    // ---- 4. a locked layer refuses loudly -----------------------------------------------------

    [Fact]
    public void LockedActiveLayer_RefusesThePaint_Loudly_AndCostsNoUndoStep()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world, locked: true);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        var warnings = new List<string>();
        using var paint = new TileGridPaintSystem(world, history, shell, warnings.Add);

        Stroke(paint, cursor, Edit(), (0, 0), (4, 2));

        Assert.Empty(data.Cells); // not one cell
        Assert.NotEmpty(warnings); // a silent no-op is indistinguishable from a dead tool
        Assert.Contains(warnings, w => w.Contains("lock", StringComparison.OrdinalIgnoreCase));
        // The refusal must not leave a dangling transaction: it would corrupt the NEXT stroke's
        // BeginTransaction, and an empty commit must record nothing.
        Assert.False(history.InTransaction);
        Assert.False(history.CanUndo);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void UnlockedAfterARefusal_TheNextStrokePaintsNormally()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world, locked: true);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        Stroke(paint, cursor, Edit(), (0, 0), (4, 0));
        Assert.Empty(data.Cells);

        grid.Get<SceneLayerComponent>().Locked = false;
        Stroke(paint, cursor, Edit(), (0, 0), (4, 0));

        Assert.Equal(5, data.Cells.Count); // the refusal was a guard, not a broken state machine
        Assert.Equal(1, history.Count);
    }

    // ---- refusals: a prefab context, and no resolvable grid -----------------------------------

    [Fact]
    public void PrefabContext_RefusesThePaint_Loudly_BecauseTerrainIsSceneContent()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var cursor = MakeCursor(world);
        MakeGizmo(world);

        // The active tab is a PREFAB context with a single resolvable root — a prefab is a class,
        // the scene's terrain grid is scene content, so the brush refuses even with a valid layer.
        var prefabRoot = world.CreateEntity();
        prefabRoot.Set(new TransformComponent(Vector2.Zero));
        prefabRoot.Set(new SceneObjectComponent());
        var shell = new EditorShellStateComponent
        {
            ActiveLayer = grid,
            ViewportTabs = new[]
            {
                new ViewportTabDescriptor(ViewportContextKind.Prefab, "tree", "tree", Closable: true),
            },
            ActiveViewportTab = 0,
        };
        var warnings = new List<string>();
        using var paint = new TileGridPaintSystem(world, history, shell, warnings.Add);

        Stroke(paint, cursor, Edit(), (0, 0), (3, 0));

        Assert.Empty(data.Cells);
        Assert.NotEmpty(warnings);
        Assert.False(history.InTransaction);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void NoResolvableGrid_WarnsAndPaintsNothing()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (_, first) = MakeGrid(world);
        var (_, second) = MakeGrid(world, new Vector2(1000, 0));
        var shell = new EditorShellStateComponent(); // no active layer
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        var warnings = new List<string>();
        using var paint = new TileGridPaintSystem(world, history, shell, warnings.Add);

        // TWO grids and no active Paint layer: the brush never guesses which terrain to edit.
        Stroke(paint, cursor, Edit(), (0, 0), (2, 0));

        Assert.Empty(first.Cells);
        Assert.Empty(second.Cells);
        Assert.NotEmpty(warnings);
        Assert.False(history.InTransaction);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void LegacySingleGridScene_IsUpgradedToAPaintLayer_AndBecomesTheActiveLayer()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world, asLayer: false); // a pre-layers "PaintGrid"
        var shell = new EditorShellStateComponent();        // nothing active yet
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        Stroke(paint, cursor, Edit(), (0, 0), (2, 0));

        Assert.Equal(3, data.Cells.Count);
        Assert.True(grid.Has<SceneLayerComponent>()); // old scenes join the layers model on first paint
        Assert.Equal(0, grid.Get<SceneLayerComponent>().Order);
        Assert.Equal(grid, shell.ActiveLayer);
        Assert.Equal(1, history.Count);
    }

    // ---- no-op edits cost nothing --------------------------------------------------------------

    [Fact]
    public void RepaintingTheSameValue_PushesNoCommand_SoTheStrokeCostsNoUndoStep()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        Stroke(paint, cursor, Edit(), (0, 0), (2, 0));
        Assert.Equal(3, data.Cells.Count);
        Assert.Equal(1, history.Count);

        // The exact same cells, the same armed value: every cell's before == after, so nothing is
        // pushed and the transaction commits EMPTY (no entry) — one undo still clears everything.
        Stroke(paint, cursor, Edit(), (0, 0), (2, 0));

        Assert.Equal(3, data.Cells.Count);
        Assert.Equal(1, history.Count);
        Assert.False(history.InTransaction);

        history.Undo();
        Assert.Empty(data.Cells);
        Assert.False(history.CanUndo);
    }

    // ---- modality + run mode -------------------------------------------------------------------

    [Fact]
    public void LeavingGroundPaintMidStroke_CommitsTheStroke_AndStopsPainting()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        var gizmo = MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        SetCursor(cursor, CellCentre(0, 0), leftPressed: true);
        paint.Update(Edit());
        Assert.Single(data.Cells);

        gizmo.Get<GizmoStateComponent>().Mode = EditorToolMode.SelectTransform;
        SetCursor(cursor, CellCentre(6, 0), leftDown: true);
        paint.Update(Edit());

        Assert.Single(data.Cells);      // the brush is inert outside its own modality
        Assert.Equal(1, history.Count); // the painted cell is a real edit: the stroke COMMITS
        Assert.False(history.InTransaction);
    }

    [Fact]
    public void EnteringPlayMidStroke_CommitsTheStroke_AndStopsPainting()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        SetCursor(cursor, CellCentre(0, 0), leftPressed: true);
        paint.Update(Edit());
        Assert.Single(data.Cells);

        SetCursor(cursor, CellCentre(6, 0), leftDown: true);
        paint.Update(Play()); // the transport hit Play: editing tools go inert

        Assert.Single(data.Cells);
        Assert.Equal(1, history.Count);
        Assert.False(history.InTransaction);
    }

    [Fact]
    public void PressOutsideTheViewport_StartsNoStroke()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, data) = MakeGrid(world);
        var shell = new EditorShellStateComponent { ActiveLayer = grid };
        var cursor = MakeCursor(world);
        MakeGizmo(world);
        using var paint = new TileGridPaintSystem(world, history, shell);

        // The press landed on the shell's chrome, not the game viewport.
        SetCursor(cursor, CellCentre(0, 0), leftPressed: true, outsideViewport: true);
        paint.Update(Edit());
        Assert.Empty(data.Cells);
        Assert.False(history.InTransaction);

        // Dragging back INTO the viewport with the button still down does not resurrect the stroke.
        SetCursor(cursor, CellCentre(1, 0), leftDown: true);
        paint.Update(Edit());
        SetCursor(cursor, CellCentre(1, 0), leftReleased: true);
        paint.Update(Edit());

        Assert.Empty(data.Cells);
        Assert.Equal(0, history.Count);
    }
}
