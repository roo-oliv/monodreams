#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Tile;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The <b>Autotile Rules workspace</b> (WS) — a full top-level editor view (the window's second
/// workspace tab, not a floating modal) with its own pane disposition:
/// <list type="bullet">
///   <item><b>Left — rule sets.</b> A scrollable list of every Indexed (Paint) layer's values:
///   the layer as a header, one row per <see cref="TilePaintValue"/> (color swatch + name;
///   click = edit it), and a <c>+ New Rule Set…</c> row per layer (routes to the name dialog —
///   a rule set IS a paintable index, so creating one here also adds its paint card).</item>
///   <item><b>Center — the 16 exposure cases.</b> Each as a 3×3 neighborhood glyph (center = the
///   value; up/right/down/left filled = SAME-value neighbor, hollow = exposed — "the tile above is
///   not wall" is the hollow top cell) plus its assigned tiles as real-art thumbnails. Click a case,
///   then click sheet cells on the right to toggle them (multiple = random alternates).</item>
///   <item><b>Right — the tileset.</b> The bound sheet (key + tile size + <c>Change Tileset…</c>,
///   which opens the asset picker) over the full sheet grid, windowed by a wheel scroll, cells
///   shrink-to-fit the pane so wide sheets stay fully reachable.</item>
/// </list>
///
/// <para><b>Edits are LIVE and undoable.</b> There is no Save/Cancel: every tile toggle / tileset
/// pick pushes a <see cref="PaintValueEditCommand"/> through the ONE shared history — the painted
/// world re-skins the same frame chain (the bake listens to <c>NotifyChanged</c>) and Ctrl+Z walks
/// rules edits like any scene edit. The <b>View: DSL</b> toggle shows the same mapping as the rule
/// string, live-synced (the identical field is hand-editable in the Inspector:
/// <c>TileGrid ▸ values ▸ autotileRules</c>).</para>
///
/// <para><b>Workspace gating.</b> <see cref="IsOpen"/> derives from
/// <see cref="EditorShellStateComponent.ActiveWorkspace"/> — the top bar's workspace tab strip (or
/// <c>workspace:*</c> ops) flips it; <see cref="Open"/> binds a specific layer AND flips (the layer
/// menu's "Edit Autotile Rules…" jump). While open, the workspace owns the pointer BELOW the top
/// bar (the top bar keeps the workspace tabs + Undo/Redo live) and the editor keyboard stands down
/// (wire <see cref="IsOpen"/> into the keys' suppression). Headless ops: <c>rules:open</c>,
/// <c>rules:mode visual|dsl</c>, <c>rules:value &lt;name&gt;</c>, <c>rules:case &lt;mask&gt;</c>,
/// <c>rules:tile &lt;col&gt;,&lt;row&gt;</c>, <c>rules:close</c> (+ <c>workspace:*</c>).</para>
/// </summary>
public sealed class AutotileRuleEditorSystem : ISystem<GameState>
{
    // ---- layout (logical points; × DPR at render) ----
    private const int Pad = 14;
    private const int LeftPaneW = 250;
    private const int RightPaneW = 420;
    private const int HeaderH = 26;
    private const int ListRowH = 24;
    private const int SwatchSize = 14;
    private const int ButtonH = 24;
    private const int CaseRowH = 30;
    private const int CaseGlyph = 24;   // the 3×3 neighborhood glyph square
    private const int ThumbSize = 24;   // an assigned tile thumbnail
    private const int MaxAlternatesShown = 4;
    private const int SheetCell = 34;   // a tileset cell button in the sheet panel
    private const int ListPool = 32;    // pooled left-list rows
    private const int DslLines = 10;

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly FileAssetTextureLoader _textures;
    private readonly EditorShellStateComponent _shell;
    private readonly EditorHistory _history;
    private readonly Action<string, EditorNotifySeverity>? _notify;
    private readonly Func<bool>? _inputBlocked; // a dialog/menu owns the pointer — stand down
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _gridSet;
    private readonly BitmapFont? _font;

    // ---- session state ----
    private Entity _layer;
    private byte _valueId;
    private bool _dslMode;
    private int _selectedCase = 15; // the "fully surrounded" interior case — a natural first edit
    private int _sheetScrollRow;
    private int _listScroll;

    /// <summary>The left pane's <c>+ New Rule Set…</c> click — the overlay wires the name dialog
    /// (a rule set is a new paintable index on that layer's grid).</summary>
    public Action<Entity>? NewRuleSetRequested;

    /// <summary>The right pane's <c>Change Tileset…</c> click — the overlay opens the asset picker
    /// anchored at the given screen point; a pick routes back through <see cref="ApplyTilesetPick"/>.</summary>
    public Action<Entity, byte, Point>? TilesetPickerRequested;

    // ---- pooled chrome ----
    private bool _built;
    private Entity _surface, _leftPaneFill, _leftHeader, _casesTitle, _modeButton, _modeLabel,
        _tilesetHeader, _tilesetKeyLabel, _tileSizeLabel, _changeButton, _changeLabel, _emptyLabel;
    private readonly List<Entity> _listBgs = new();
    private readonly List<Entity> _listSwatches = new();
    private readonly List<Entity> _listLabels = new();
    private readonly List<Entity> _caseButtons = new();
    private readonly List<Entity> _caseGlyphs = new();   // one mesh per case (3×3 quads)
    private readonly List<Entity> _caseThumbs = new();   // MaxAlternatesShown per case
    private readonly List<Entity> _sheetButtons = new(); // pooled tileset cell thumbs
    private readonly List<Entity> _dslLineLabels = new();

    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether the workspace is the active view (owns pointer-below-top-bar + keyboard).</summary>
    public bool IsOpen => _shell.ActiveWorkspace == EditorWorkspace.AutotileRules;

    /// <summary>The bound rule set's layer (the <c>rules:new</c>/<c>rules:tileset</c> op target).</summary>
    public Entity CurrentLayer => _layer;

    /// <summary>The bound rule set's value id (see <see cref="CurrentLayer"/>).</summary>
    public byte CurrentValueId => _valueId;

    public AutotileRuleEditorSystem(World world, ViewportManager viewportManager,
        FileAssetTextureLoader textures, BitmapFont? font,
        EditorShellStateComponent shellState, EditorHistory history,
        Action<string, EditorNotifySeverity>? notify = null,
        Func<bool>? inputBlocked = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _shell = shellState ?? throw new ArgumentNullException(nameof(shellState));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _font = font;
        _notify = notify;
        _inputBlocked = inputBlocked;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _gridSet = world.GetEntities().With<TileGridComponent>().AsSet();
    }

    // ─── public surface (menu + ops + tabs) ──────────────────────────────────────────────────────

    /// <summary>Enters the workspace bound to <paramref name="paintLayer"/> (the layer menu's
    /// "Edit Autotile Rules…" jump). An invalid layer still enters — the view auto-binds.</summary>
    public void Open(Entity paintLayer)
    {
        if (paintLayer.IsAlive && paintLayer.Has<TileGridComponent>())
        {
            _layer = paintLayer;
            _valueId = FirstEditableValueId(paintLayer);
        }
        _shell.ActiveWorkspace = EditorWorkspace.AutotileRules;
    }

    /// <summary>Enters the workspace with the current binding (the workspace tab's plain switch).</summary>
    public void OpenWorkspace() => _shell.ActiveWorkspace = EditorWorkspace.AutotileRules;

    /// <summary>Leaves back to the Level Editor workspace.</summary>
    public void Close() => _shell.ActiveWorkspace = EditorWorkspace.LevelEditor;

    public void SetMode(bool dsl) => _dslMode = dsl;

    /// <summary>Selects the value named <paramref name="name"/> across ALL grids (the
    /// <c>rules:value</c> op + the left list's headless twin).</summary>
    public bool SelectValueByName(string name)
    {
        foreach (var grid in OrderedGrids())
        {
            foreach (var value in grid.Get<TileGridComponent>().Values)
            {
                if (!string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                _layer = grid;
                _valueId = value.Id;
                return true;
            }
        }
        return false;
    }

    /// <summary>Selects a specific rule set (a left-list row click).</summary>
    public void SelectValue(Entity layer, byte valueId)
    {
        if (!layer.IsAlive || !layer.Has<TileGridComponent>()) return;
        _layer = layer;
        _valueId = valueId;
        _sheetScrollRow = 0;
    }

    public void SelectCase(int mask) => _selectedCase = Math.Clamp(mask, 0, 15);

    /// <summary>Toggles tileset cell (<paramref name="col"/>, <paramref name="row"/>) on the selected
    /// case — LIVE: one undoable <see cref="PaintValueEditCommand"/>; the bake re-skins next frame.</summary>
    public void ToggleTile(int col, int row)
    {
        var value = CurrentValue();
        if (value == null || !_layer.IsAlive) return;
        var table = TileGridBaking.ParseRules(value.AutotileRules);
        var current = new List<Point>(table[_selectedCase] ?? Array.Empty<Point>());
        var cell = new Point(col, row);
        if (!current.Remove(cell)) current.Add(cell);
        if (current.Count == 0) current.Add(Point.Zero);
        table[_selectedCase] = current.ToArray();
        _history.Push(PaintValueEditCommand.Rules(_layer, value, SerializeRules(table)));
    }

    /// <summary>Binds a picked tileset (key + tile size) to a rule set — the asset picker's confirm
    /// (one undoable command; rules keep — masks are sheet-agnostic).</summary>
    public void ApplyTilesetPick(Entity layer, byte valueId, string tilesetKey, int tileSize)
    {
        if (!layer.IsAlive || !layer.Has<TileGridComponent>()) return;
        var value = layer.Get<TileGridComponent>().FindValue(valueId);
        if (value == null) return;
        _history.Push(PaintValueEditCommand.Tileset(layer, value, tilesetKey, Math.Max(1, tileSize)));
        _notify?.Invoke($"'{value.Name}' now maps '{tilesetKey}'", EditorNotifySeverity.Success);
    }

    /// <summary>The canonical DSL for a rules table: all 16 cases, alternates joined by <c>|</c> —
    /// the exact grammar <see cref="TileGridBaking.ParseRules"/> reads.</summary>
    public static string SerializeRules(Point[][] table)
    {
        var sb = new StringBuilder();
        for (var mask = 0; mask < 16; mask++)
        {
            if (mask > 0) sb.Append(' ');
            sb.Append(mask).Append(':');
            var alternates = table[mask] is { Length: > 0 } a ? a : new[] { Point.Zero };
            for (var i = 0; i < alternates.Length; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(alternates[i].X).Append(',').Append(alternates[i].Y);
            }
        }
        return sb.ToString();
    }

    // ─── internals ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Every grid-bearing entity (an Indexed/Paint layer, or a legacy loose grid), scene
    /// layer order first (front-most on top — the Entities panel's convention), legacy grids after.</summary>
    private List<Entity> OrderedGrids()
    {
        var layered = new List<Entity>();
        var loose = new List<Entity>();
        foreach (var e in _gridSet.GetEntities())
            (e.Has<SceneLayerComponent>() ? layered : loose).Add(e);
        layered.Sort(MonoDreams.System.Level.SceneLayerSystem.CompareLayers);
        layered.Reverse();
        layered.AddRange(loose);
        return layered;
    }

    private static byte FirstEditableValueId(Entity grid)
    {
        var values = grid.Get<TileGridComponent>().Values;
        foreach (var v in values)
            if (!string.IsNullOrEmpty(v.TilesetKey)) return v.Id;
        return values.Count > 0 ? values[0].Id : (byte)0;
    }

    private TilePaintValue? CurrentValue() =>
        _layer.IsAlive && _layer.Has<TileGridComponent>()
            ? _layer.Get<TileGridComponent>().FindValue(_valueId)
            : null;

    /// <summary>Heals the binding every frame: a dead layer / removed value re-binds to the first
    /// available rule set (undo/redo and deletes can invalidate the selection under the view).</summary>
    private void HealBinding()
    {
        if (_layer.IsAlive && _layer.Has<TileGridComponent>() && CurrentValue() != null) return;
        _layer = default;
        foreach (var grid in OrderedGrids())
        {
            if (grid.Get<TileGridComponent>().Values.Count == 0) continue;
            _layer = grid;
            _valueId = FirstEditableValueId(grid);
            return;
        }
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        if (!IsOpen)
        {
            if (_built) ParkAll();
            return;
        }
        if (!_built) BuildChrome();
        HealBinding();

        var scale = _viewportManager.DevicePixelRatio;
        var surface = SurfaceRect(scale);
        var rows = BuildListRows();
        HandleInput(surface, rows, scale, state);
        if (!IsOpen) { ParkAll(); return; }
        LayoutChrome(surface, rows, scale);
    }

    // ─── geometry ────────────────────────────────────────────────────────────────────────────────

    private static int Px(int points, float scale) => EditorChromeLayout.Px(points, scale);

    /// <summary>The workspace surface: the whole window between the top bar (the workspace tabs +
    /// Undo/Redo stay live above) and the status bar (notifications stay visible below).</summary>
    private Rectangle SurfaceRect(float scale)
    {
        var top = EditorChromeLayout.TopBar(_viewportManager.ScreenWidth, scale).Bottom;
        var statusTop = EditorChromeLayout.StatusBar(
            _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale).Y;
        return new Rectangle(0, top, Math.Max(1, _viewportManager.ScreenWidth), Math.Max(1, statusTop - top));
    }

    private Rectangle LeftPaneRect(Rectangle surface, float scale) =>
        new(surface.X, surface.Y, Px(LeftPaneW, scale), surface.Height);

    private Rectangle RightPaneRect(Rectangle surface, float scale) =>
        new(surface.Right - Px(RightPaneW, scale), surface.Y, Px(RightPaneW, scale), surface.Height);

    private Rectangle CenterRect(Rectangle surface, float scale)
    {
        var left = LeftPaneRect(surface, scale).Right + Px(Pad, scale);
        var right = RightPaneRect(surface, scale).X - Px(Pad, scale);
        return new Rectangle(left, surface.Y + Px(Pad, scale),
            Math.Max(1, right - left), Math.Max(1, surface.Height - Px(Pad * 2, scale)));
    }

    private Rectangle ModeButtonRect(Rectangle center, float scale) => new(
        center.Right - Px(110, scale), center.Y, Px(110, scale), Px(ButtonH, scale));

    private Rectangle CaseRowRect(Rectangle center, int mask, float scale) => new(
        center.X,
        center.Y + Px(HeaderH + 8, scale) + mask * Px(CaseRowH, scale),
        Px(CaseGlyph + 10 + 34 + MaxAlternatesShown * (ThumbSize + 4), scale),
        Px(CaseRowH - 4, scale));

    private Rectangle ChangeTilesetRect(Rectangle rightPane, float scale) => new(
        rightPane.X + Px(Pad, scale),
        rightPane.Y + Px(Pad + HeaderH + ListRowH, scale),
        Px(150, scale), Px(ButtonH, scale));

    private Rectangle SheetRect(Rectangle rightPane, float scale)
    {
        var top = rightPane.Y + Px(Pad + HeaderH + ListRowH + ButtonH + 10, scale);
        return new Rectangle(
            rightPane.X + Px(Pad, scale), top,
            rightPane.Width - Px(Pad * 2, scale),
            Math.Max(1, rightPane.Bottom - Px(Pad, scale) - top));
    }

    /// <summary>The sheet panel's cell pixel size: the standard size, shrunk to FIT the sheet's
    /// full column count in the pane — a wide tileset (17 columns) stays fully reachable.</summary>
    private int SheetCellPx(Rectangle sheet, int cols, float scale) =>
        Math.Max(8, Math.Min(Px(SheetCell, scale), sheet.Width / Math.Max(1, cols)));

    /// <summary>The tileset's cell grid dimensions (from the loaded texture + the value's TileSize).</summary>
    private (int Cols, int Rows) SheetDims()
    {
        var value = CurrentValue();
        if (value?.TilesetKey == null) return (1, 1);
        var texture = _textures.Load(value.TilesetKey);
        if (texture == null) return (1, 1);
        var size = Math.Max(1, value.TileSize);
        return (Math.Max(1, texture.Width / size), Math.Max(1, texture.Height / size));
    }

    private bool TrySheetCellAt(Rectangle sheet, Point point, float scale, out int col, out int row)
    {
        col = row = 0;
        if (!sheet.Contains(point)) return false;
        // No bound sheet ⇒ the pane draws nothing, so a click there must NOT pick a phantom cell (0,0)
        // and push an invisible rules edit onto the shared history. Pick a tileset first.
        var bound = CurrentValue();
        if (bound?.TilesetKey == null || _textures.Load(bound.TilesetKey) == null) return false;
        var dims = SheetDims();
        var cell = SheetCellPx(sheet, dims.Cols, scale);
        col = (point.X - sheet.X) / cell;
        row = (point.Y - sheet.Y) / cell + _sheetScrollRow;
        return col >= 0 && col < dims.Cols && row >= 0 && row < dims.Rows;
    }

    // ─── the left rule-set list (pure rows, pooled visuals) ─────────────────────────────────────

    private enum ListRowKind { LayerHeader, Value, NewValue, EmptyHint }

    private readonly struct ListRow
    {
        public ListRow(ListRowKind kind, Entity layer, byte valueId, string label, Color swatch)
        {
            Kind = kind; Layer = layer; ValueId = valueId; Label = label; Swatch = swatch;
        }

        public ListRowKind Kind { get; }
        public Entity Layer { get; }
        public byte ValueId { get; }
        public string Label { get; }
        public Color Swatch { get; }
    }

    private List<ListRow> BuildListRows()
    {
        var rows = new List<ListRow>();
        foreach (var grid in OrderedGrids())
        {
            var name = MonoDreams.System.Level.SceneLayerSystem.LayerName(grid);
            rows.Add(new ListRow(ListRowKind.LayerHeader, grid, 0, name, Color.Transparent));
            foreach (var value in grid.Get<TileGridComponent>().Values)
                rows.Add(new ListRow(ListRowKind.Value, grid, value.Id, value.Name, value.Color));
            rows.Add(new ListRow(ListRowKind.NewValue, grid, 0, "+ New Rule Set...", Color.Transparent));
        }
        if (rows.Count == 0)
            rows.Add(new ListRow(ListRowKind.EmptyHint, default, 0, "No Indexed Layers in this scene", Color.Transparent));
        return rows;
    }

    private Rectangle ListRowRect(Rectangle leftPane, int visibleIndex, float scale) => new(
        leftPane.X + Px(8, scale),
        leftPane.Y + Px(Pad + HeaderH, scale) + visibleIndex * Px(ListRowH, scale),
        leftPane.Width - Px(16, scale),
        Px(ListRowH - 2, scale));

    private int VisibleListRows(Rectangle leftPane, float scale) =>
        Math.Max(1, (leftPane.Height - Px(Pad + HeaderH + Pad, scale)) / Px(ListRowH, scale));

    // ─── input ───────────────────────────────────────────────────────────────────────────────────

    private void HandleInput(Rectangle surface, List<ListRow> rows, float scale, GameState state)
    {
        // A dialog or a popup menu owns the pointer (they weave earlier and consume) — stand down
        // entirely so a picker click never ALSO lands in the workspace beneath it.
        if (_inputBlocked?.Invoke() ?? false) return;

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!surface.Contains(point)) return; // the top bar (tabs, Undo/Redo) stays live

            var leftPane = LeftPaneRect(surface, scale);
            var center = CenterRect(surface, scale);
            var rightPane = RightPaneRect(surface, scale);
            var sheet = SheetRect(rightPane, scale);

            if (input.LeftButtonReleased)
            {
                if (leftPane.Contains(point))
                {
                    var visible = VisibleListRows(leftPane, scale);
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var vi = i - _listScroll;
                        if (vi < 0 || vi >= visible) continue;
                        if (!ListRowRect(leftPane, vi, scale).Contains(point)) continue;
                        var row = rows[i];
                        switch (row.Kind)
                        {
                            case ListRowKind.Value: SelectValue(row.Layer, row.ValueId); break;
                            case ListRowKind.NewValue: NewRuleSetRequested?.Invoke(row.Layer); break;
                        }
                        break;
                    }
                }
                else if (ModeButtonRect(center, scale).Contains(point))
                {
                    _dslMode = !_dslMode;
                }
                else if (ChangeTilesetRect(rightPane, scale).Contains(point)
                         && _layer.IsAlive && CurrentValue() != null)
                {
                    var anchor = ChangeTilesetRect(rightPane, scale);
                    TilesetPickerRequested?.Invoke(_layer, _valueId, new Point(anchor.X, anchor.Bottom));
                }
                else if (!_dslMode)
                {
                    for (var mask = 0; mask < 16; mask++)
                        if (CaseRowRect(center, mask, scale).Contains(point)) { _selectedCase = mask; break; }
                    if (TrySheetCellAt(sheet, point, scale, out var col, out var row))
                        ToggleTile(col, row);
                }
            }

            if (input.ScrollWheelDelta != 0)
            {
                if (leftPane.Contains(point))
                    _listScroll = Math.Max(0, _listScroll - Math.Sign(input.ScrollWheelDelta));
                else if (!_dslMode && sheet.Contains(point))
                    _sheetScrollRow = Math.Max(0, _sheetScrollRow - Math.Sign(input.ScrollWheelDelta));
                input.ScrollWheelDelta = 0; // the shell/panels beneath must not also scroll
            }

            // The workspace owns the pointer below the top bar: swallow edges so nothing beneath
            // (panels, palette, gizmo, selection) re-picks or places.
            input.LeftButtonPressed = false;
            input.LeftButtonReleased = false;
            input.RightButtonPressed = false;
            input.RightButtonReleased = false;
            cursor.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    // ─── layout (pooled; parked when closed) ─────────────────────────────────────────────────────

    private void BuildChrome()
    {
        _surface = CreateFill(EditorTheme.Bg0, EditorTheme.Bg0, EditorTheme.Depths.DialogBackdrop);
        _leftPaneFill = CreateFill(EditorTheme.Bg1, EditorTheme.Border, EditorTheme.Depths.DialogPanel);
        _leftHeader = CreateLabel();
        _casesTitle = CreateLabel();
        _tilesetHeader = CreateLabel();
        _tilesetKeyLabel = CreateLabel();
        _tileSizeLabel = CreateLabel();
        _emptyLabel = CreateLabel();
        (_modeButton, _modeLabel) = CreateButton();
        (_changeButton, _changeLabel) = CreateButton();

        for (var i = 0; i < ListPool; i++)
        {
            _listBgs.Add(CreateMesh(EditorTheme.Depths.DialogPanel + 0.01f));
            // The per-row swatch fill is overwritten with the value's own color each layout pass; the
            // build-time fill is the neutral role (the module's colors are all EditorTheme roles).
            _listSwatches.Add(CreateFill(EditorTheme.NeutralTint, EditorTheme.Border,
                EditorTheme.Depths.DialogControl));
            _listLabels.Add(CreateLabel());
        }
        for (var mask = 0; mask < 16; mask++)
        {
            var (button, _) = CreateButton(withLabel: false);
            _caseButtons.Add(button);
            _caseGlyphs.Add(CreateMesh(EditorTheme.Depths.DialogControl + 0.002f));
            for (var t = 0; t < MaxAlternatesShown; t++)
                _caseThumbs.Add(CreateThumb());
        }
        for (var i = 0; i < 200; i++)
            _sheetButtons.Add(CreateThumb());
        for (var i = 0; i < DslLines; i++)
            _dslLineLabels.Add(CreateLabel());
        _built = true;
    }

    private void LayoutChrome(Rectangle surface, List<ListRow> rows, float scale)
    {
        var labelH = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var leftPane = LeftPaneRect(surface, scale);
        var center = CenterRect(surface, scale);
        var rightPane = RightPaneRect(surface, scale);

        Place(_surface, surface.X, surface.Y);
        SetButtonVisual(_surface, surface.Width, surface.Height, EditorTheme.Bg0, EditorTheme.Bg0);
        Place(_leftPaneFill, leftPane.X, leftPane.Y);
        SetButtonVisual(_leftPaneFill, leftPane.Width, leftPane.Height, EditorTheme.Bg1, EditorTheme.Border);

        PlaceLabel(_leftHeader, "RULE SETS", leftPane.X + Px(Pad, scale), leftPane.Y + Px(Pad, scale), scale,
            EditorTheme.Text1);

        LayoutList(leftPane, rows, labelH, scale);

        var value = CurrentValue();
        if (value == null || !_layer.IsAlive)
        {
            // Empty state: no rule set anywhere. The list already shows the hint row; park the panes.
            ParkCasesAndSheet();
            Park(_modeButton); Park(_modeLabel);
            Park(_changeButton); Park(_changeLabel);
            Park(_tilesetHeader); Park(_tilesetKeyLabel); Park(_tileSizeLabel);
            foreach (var line in _dslLineLabels) Park(line);
            PlaceLabel(_emptyLabel, "Select or create a rule set on an Indexed Layer",
                center.X, center.Y + Px(HeaderH, scale), scale, EditorTheme.TextMuted);
            return;
        }
        Park(_emptyLabel);

        var layerName = MonoDreams.System.Level.SceneLayerSystem.LayerName(_layer);
        PlaceLabel(_casesTitle, $"{layerName} / {value.Name}", center.X, center.Y
            + (Px(ButtonH, scale) - labelH) / 2f, scale, EditorTheme.Text0);
        PlaceButtonWithLabel(_modeButton, _modeLabel, ModeButtonRect(center, scale),
            _dslMode ? "View: DSL" : "View: Visual", labelH, scale, selected: false);

        var table = TileGridBaking.ParseRules(value.AutotileRules);

        if (_dslMode)
        {
            ParkCasesAndSheet();
            Park(_changeButton); Park(_changeLabel);
            Park(_tilesetHeader); Park(_tilesetKeyLabel); Park(_tileSizeLabel);
            var dsl = SerializeRules(table);
            var budget = Math.Max(24, (surface.Right - Px(Pad, scale) - center.X) / Math.Max(6, (int)(8 * scale)));
            var y = center.Y + Px(HeaderH + 8, scale);
            for (var i = 0; i < _dslLineLabels.Count; i++)
            {
                var start = i * budget;
                var text = start < dsl.Length ? dsl.Substring(start, Math.Min(budget, dsl.Length - start)) : "";
                PlaceLabel(_dslLineLabels[i], text, center.X, y + i * (labelH + Px(4, scale)), scale,
                    EditorTheme.Text0);
            }
            return;
        }
        foreach (var line in _dslLineLabels) Park(line);

        // Right pane: the tileset binding + the sheet grid.
        PlaceLabel(_tilesetHeader, "TILESET", rightPane.X + Px(Pad, scale), rightPane.Y + Px(Pad, scale),
            scale, EditorTheme.Text1);
        var keyText = value.TilesetKey ?? "(none - pick a sheet)";
        PlaceLabel(_tilesetKeyLabel, EditorPanelModel.MiddleEllipsis(keyText, 44),
            rightPane.X + Px(Pad, scale), rightPane.Y + Px(Pad + HeaderH, scale), scale,
            value.TilesetKey == null ? EditorTheme.Warning : EditorTheme.Text0);
        PlaceButtonWithLabel(_changeButton, _changeLabel, ChangeTilesetRect(rightPane, scale),
            "Change Tileset...", labelH, scale, selected: false);
        var change = ChangeTilesetRect(rightPane, scale);
        PlaceLabel(_tileSizeLabel, $"Tile: {value.TileSize} px",
            change.Right + Px(10, scale), change.Y + (change.Height - labelH) / 2f, scale, EditorTheme.TextMuted);

        var texture = value.TilesetKey != null ? _textures.Load(value.TilesetKey) : null;
        var tileSize = Math.Max(1, value.TileSize);

        // Case rows: glyph + assigned thumbs.
        for (var mask = 0; mask < 16; mask++)
        {
            var row = CaseRowRect(center, mask, scale);
            var selected = mask == _selectedCase;
            Place(_caseButtons[mask], row.X, row.Y);
            SetButtonVisual(_caseButtons[mask], row.Width, row.Height,
                selected ? EditorTheme.Bg3 : EditorTheme.Bg2,
                selected ? EditorTheme.Accent : EditorTheme.Border);

            SetGlyphMesh(_caseGlyphs[mask], mask, new Rectangle(
                row.X + Px(3, scale), row.Y + Px(2, scale), Px(CaseGlyph, scale), Px(CaseGlyph, scale)));

            var alternates = table[mask] ?? Array.Empty<Point>();
            for (var t = 0; t < MaxAlternatesShown; t++)
            {
                var thumb = _caseThumbs[mask * MaxAlternatesShown + t];
                if (texture == null || t >= alternates.Length) { ParkThumb(thumb); continue; }
                var dest = new Rectangle(
                    row.X + Px(CaseGlyph + 10 + 34, scale) + t * Px(ThumbSize + 4, scale),
                    row.Y + Px(2, scale), Px(ThumbSize, scale), Px(ThumbSize, scale));
                PlaceThumb(thumb, texture,
                    new Rectangle(alternates[t].X * tileSize, alternates[t].Y * tileSize, tileSize, tileSize), dest);
            }
        }

        // The tileset sheet grid (windowed by the scroll row; cells shrink to fit the pane width).
        var dims = SheetDims();
        var sheet = SheetRect(rightPane, scale);
        var cell = SheetCellPx(sheet, dims.Cols, scale);
        var visibleRows = Math.Max(1, sheet.Height / cell);
        _sheetScrollRow = Math.Clamp(_sheetScrollRow, 0, Math.Max(0, dims.Rows - visibleRows));
        var selectedAlternates = new HashSet<Point>(table[_selectedCase] ?? Array.Empty<Point>());

        for (var i = 0; i < _sheetButtons.Count; i++)
        {
            var col = i % Math.Max(1, dims.Cols);
            var rowOnScreen = i / Math.Max(1, dims.Cols);
            var row = rowOnScreen + _sheetScrollRow;
            var thumb = _sheetButtons[i];
            if (texture == null || col >= dims.Cols || row >= dims.Rows || rowOnScreen >= visibleRows)
            {
                ParkThumb(thumb);
                continue;
            }
            var dest = new Rectangle(sheet.X + col * cell, sheet.Y + rowOnScreen * cell,
                cell - Px(2, scale), cell - Px(2, scale));
            PlaceThumb(thumb, texture, new Rectangle(col * tileSize, row * tileSize, tileSize, tileSize), dest,
                tint: selectedAlternates.Contains(new Point(col, row))
                    ? EditorTheme.Accent
                    : EditorTheme.NeutralTint);
        }
    }

    private void LayoutList(Rectangle leftPane, List<ListRow> rows, float labelH, float scale)
    {
        var visible = VisibleListRows(leftPane, scale);
        _listScroll = Math.Clamp(_listScroll, 0, Math.Max(0, rows.Count - visible));
        for (var slot = 0; slot < ListPool; slot++)
        {
            var i = slot + _listScroll;
            if (slot >= visible || i >= rows.Count)
            {
                ClearMesh(_listBgs[slot]);
                Park(_listSwatches[slot]);
                Park(_listLabels[slot]);
                continue;
            }

            var row = rows[i];
            var rect = ListRowRect(leftPane, slot, scale);
            var isSelected = row.Kind == ListRowKind.Value && row.Layer == _layer && row.ValueId == _valueId;

            if (isSelected)
                SetMeshAt(_listBgs[slot], new FilledRectangleMeshGenerator(rect, EditorTheme.AccentSoft).Generate(),
                    EditorTheme.Depths.DialogPanel + 0.01f);
            else
                ClearMesh(_listBgs[slot]);

            var textX = rect.X + Px(6, scale);
            if (row.Kind == ListRowKind.Value)
            {
                var swatch = new Rectangle(rect.X + Px(4, scale),
                    rect.Y + (rect.Height - Px(SwatchSize, scale)) / 2, Px(SwatchSize, scale), Px(SwatchSize, scale));
                Place(_listSwatches[slot], swatch.X, swatch.Y);
                SetButtonVisual(_listSwatches[slot], swatch.Width, swatch.Height, row.Swatch,
                    isSelected ? EditorTheme.Accent : EditorTheme.Border);
                textX = swatch.Right + Px(8, scale);
            }
            else
            {
                Park(_listSwatches[slot]);
            }

            var color = row.Kind switch
            {
                ListRowKind.LayerHeader => EditorTheme.Text1,
                ListRowKind.NewValue => EditorTheme.Accent,
                ListRowKind.EmptyHint => EditorTheme.TextMuted,
                _ => isSelected ? EditorTheme.Text0 : EditorTheme.Text1,
            };
            var indent = row.Kind == ListRowKind.LayerHeader ? 0 : Px(6, scale);
            PlaceLabel(_listLabels[slot], row.Label, textX + indent,
                rect.Y + (rect.Height - labelH) / 2f, scale, color);
        }
    }

    /// <summary>The 3×3 neighborhood glyph for <paramref name="mask"/>: center = Accent; the four
    /// edge cells filled when that neighbor is SAME (bit set), hollow-dark when different; corners
    /// inert. The visual reading of "6:0,0" — right+down same, top+left exposed.</summary>
    private void SetGlyphMesh(Entity mesh, int mask, Rectangle box)
    {
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var offset = 0;
        var cw = box.Width / 3;
        void Cell(int cx, int cy, Color color)
        {
            var r = new Rectangle(box.X + cx * cw, box.Y + cy * cw, cw - 1, cw - 1);
            vertices.Add(new VertexPositionColor(new Vector3(r.Left, r.Top, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(r.Right, r.Top, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(r.Right, r.Bottom, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(r.Left, r.Bottom, 0), color));
            indices.Add(offset); indices.Add(offset + 1); indices.Add(offset + 2);
            indices.Add(offset); indices.Add(offset + 2); indices.Add(offset + 3);
            offset += 4;
        }

        var same = EditorTheme.Text1;
        var different = EditorTheme.Bg4;
        Cell(1, 1, EditorTheme.Accent);
        Cell(1, 0, (mask & TileGridBaking.MaskUp) != 0 ? same : different);
        Cell(2, 1, (mask & TileGridBaking.MaskRight) != 0 ? same : different);
        Cell(1, 2, (mask & TileGridBaking.MaskDown) != 0 ? same : different);
        Cell(0, 1, (mask & TileGridBaking.MaskLeft) != 0 ? same : different);

        var draw = mesh.Get<DrawComponent>();
        draw.Vertices = vertices.ToArray();
        draw.Indices = indices.ToArray();
        draw.PrimitiveType = PrimitiveType.TriangleList;
    }

    // ─── pooled-entity helpers (the palette/dialog idioms) ───────────────────────────────────────

    private Entity CreateFill(Color fill, Color border, float depth)
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        e.Set(new SimpleButtonComponent
        {
            Size = Vector2.One, FillColor = fill, Color = border, LineThickness = 1f,
            Target = RenderTargetID.Editor, LayerDepth = depth,
        });
        return e;
    }

    private (Entity Button, Entity Label) CreateButton(bool withLabel = true)
    {
        var label = withLabel ? CreateLabel() : default;
        var button = _world.CreateEntity();
        button.Set(new EditorInfrastructureComponent());
        button.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        button.Set(new SimpleButtonComponent
        {
            Size = Vector2.One, FillColor = EditorTheme.Bg2, Color = EditorTheme.Border, LineThickness = 1f,
            TextEntity = label, Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.DialogControl,
        });
        return (button, label);
    }

    private Entity CreateLabel()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        e.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.DialogLabel,
            TextContent = string.Empty,
            Color = EditorTheme.Text1,
            Font = _font!,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        return e;
    }

    private Entity CreateMesh(float depth)
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh, Target = RenderTargetID.Editor, LayerDepth = depth,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(), Indices = Array.Empty<int>(),
        });
        return e;
    }

    private Entity CreateThumb()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite, Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.DialogControl + 0.003f,
        });
        return e;
    }

    private static void PlaceThumb(Entity thumb, Texture2D texture, Rectangle source, Rectangle dest,
        Color? tint = null)
    {
        var draw = thumb.Get<DrawComponent>();
        draw.Texture = texture;
        draw.SourceRectangle = source;
        draw.Position = new Vector2(dest.X, dest.Y);
        draw.Size = new Vector2(dest.Width, dest.Height);
        draw.Origin = Vector2.Zero;
        draw.Rotation = 0f;
        draw.Color = tint ?? EditorTheme.NeutralTint;
    }

    private static void ParkThumb(Entity thumb)
    {
        if (!thumb.IsAlive) return;
        thumb.Get<DrawComponent>().Texture = null;
    }

    private void PlaceButtonWithLabel(Entity button, Entity label, Rectangle rect, string text,
        float labelH, float scale, bool selected)
    {
        Place(button, rect.X, rect.Y);
        SetButtonVisual(button, rect.Width, rect.Height,
            selected ? EditorTheme.Bg3 : EditorTheme.Bg2, selected ? EditorTheme.Accent : EditorTheme.Border);
        PlaceLabel(label, text, rect.X + Px(8, scale), rect.Y + (rect.Height - labelH) / 2f, scale,
            EditorTheme.Text0);
    }

    private void SetButtonVisual(Entity button, float w, float h, Color fill, Color border)
    {
        ref var visual = ref button.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(w, h);
        visual.FillColor = fill;
        visual.Color = border;
    }

    private void PlaceLabel(Entity label, string text, float x, float y, float scale, Color? color = null)
    {
        Place(label, x, y);
        ref var dynamicText = ref label.Get<DynamicTextComponent>();
        dynamicText.TextContent = text;
        dynamicText.Scale = EditorChromeBuilder.LabelScale * scale;
        dynamicText.VisibleCharacterCount = int.MaxValue;
        if (color.HasValue) dynamicText.Color = color.Value;
    }

    private static void Place(Entity e, float x, float y)
    {
        ref var transform = ref e.Get<TransformComponent>();
        transform.Position = new Vector2(x, y);
        e.NotifyChanged<TransformComponent>();
    }

    private static void Park(Entity e)
    {
        if (!e.IsAlive) return;
        if (e.Has<TransformComponent>())
        {
            ref var transform = ref e.Get<TransformComponent>();
            transform.Position = SystemsPanelLayout.ParkedPosition;
            e.NotifyChanged<TransformComponent>();
        }
        if (e.Has<DrawComponent>() && e.Get<DrawComponent>().Type == DrawElementType.Sprite)
            e.Get<DrawComponent>().Texture = null;
    }

    private static void SetMeshAt(Entity e, MeshData mesh, float depth)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.Target = RenderTargetID.Editor;
        dc.LayerDepth = depth;
    }

    private static void ClearMesh(Entity e)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Vertices = Array.Empty<VertexPositionColor>();
        dc.Indices = Array.Empty<int>();
    }

    private void ParkCasesAndSheet()
    {
        foreach (var b in _caseButtons) Park(b);
        foreach (var g in _caseGlyphs) if (g.IsAlive) ClearMesh(g);
        foreach (var t in _caseThumbs) ParkThumb(t);
        foreach (var s in _sheetButtons) ParkThumb(s);
    }

    private void ParkAll()
    {
        Park(_surface); Park(_leftPaneFill); Park(_leftHeader); Park(_casesTitle);
        Park(_modeButton); Park(_modeLabel);
        Park(_tilesetHeader); Park(_tilesetKeyLabel); Park(_tileSizeLabel);
        Park(_changeButton); Park(_changeLabel);
        Park(_emptyLabel);
        for (var i = 0; i < _listBgs.Count; i++)
        {
            ClearMesh(_listBgs[i]);
            Park(_listSwatches[i]);
            Park(_listLabels[i]);
        }
        foreach (var line in _dslLineLabels) Park(line);
        ParkCasesAndSheet();
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        _gridSet.Dispose();
    }
}
