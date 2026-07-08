#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor's context-menu primitive (UX2-D §4): a popup list on the native-resolution
/// <c>RenderTargetID.Editor</c> target — items (label, enabled, danger), separators, and ONE level of
/// submenus ("Order ▸" opens beside) — built from a pure <see cref="EditorContextMenuModel"/> item list
/// and laid out by the pure <see cref="EditorContextMenuLayout"/>. The SAME model renders as a
/// right-click context menu (<see cref="OpenAt"/>, at the cursor) or a header dropdown
/// (<see cref="OpenBelow"/>, anchored under a button) — one model, two anchors. Closed by an item click,
/// a click-away, or Escape.
///
/// <para><b>Modality (owns the pointer while open, like the dialog).</b> Each open frame it hit-tests
/// its own items FIRST, then <b>consumes the cursor's pointer edges</b> (clears press/release/scroll on
/// the single cursor entity), so no mouse-driven editor system downstream that frame acts on the same
/// click — the mouse half of the modal capture, exactly the <see cref="EditorDialogSystem"/> pattern.
/// The screen weaves this entry immediately AFTER <c>editor.dialog</c> so that, in the rare case both
/// could open, the dialog consumes first and wins (and the overlay's open paths refuse to open a menu
/// while the dialog is open). The keyboard half is the screen wiring the host keyboard system's
/// <c>ShouldSuppressInput</c> to <c>Dialog.IsOpen || Menu.IsOpen</c>, so Escape closes the menu instead
/// of quitting the game.</para>
///
/// <para><b>Chrome rules.</b> The menu box / item hover fills / separators are
/// <see cref="SimpleButtonComponent"/> meshes (prepped by the woven <c>ButtonMeshPrepSystem</c>), labels
/// are <see cref="DynamicTextComponent"/>, and a submenu ▸ caret is a screen-baked triangle mesh — all
/// on the Editor target, identity <c>WorldMatrix</c>, tagged <see cref="EditorInfrastructureComponent"/>,
/// carrying NO <c>VisibleComponent</c>, shown/hidden by parking off-screen (the SystemsPanel idiom).
/// Colours/depths come from <see cref="EditorTheme"/> (items use the UX-A state model: hover fill,
/// <c>Danger</c> label for destructive items, <c>TextDisabled</c> when disabled); the menu occupies the
/// dedicated <c>EditorTheme.Depths.Menu*</c> band above the tooltip so it is never occluded.</para>
///
/// <para><b>Headless-drivable.</b> <see cref="OpenAt"/> / <see cref="OpenBelow"/> / <see cref="Pick"/> /
/// <see cref="Close"/> drive the full flow with no real mouse (the <c>menu:*</c> op grammar). The menu
/// itself is game-agnostic: a clicked/picked item fires its action-id <see cref="EditorMenuItem.Path"/>
/// through the <c>dispatch</c> callback the overlay supplies.</para>
/// </summary>
public sealed class EditorContextMenuSystem : ISystem<GameState>
{
    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Action<string, GameState> _dispatch;
    private readonly Func<KeyboardState> _getKeyboardState;
    private readonly Func<bool>? _isBlocked;
    private readonly EntitySet _cursorSet;

    private bool _open;
    private IReadOnlyList<EditorMenuItem> _items = Array.Empty<EditorMenuItem>();
    private Point _anchor;
    private int _openSubmenuIndex = -1; // the expanded submenu-parent item index, or -1
    private int _hoverMain = -1;
    private int _hoverSub = -1;
    private bool _leftDown;
    private KeyboardState _prevKeys;

    private bool _built;
    private Entity _menuBox, _subBox;
    private readonly List<MenuRowVisual> _mainPool = new();
    private readonly List<MenuRowVisual> _subPool = new();

    public bool IsEnabled { get; set; } = true;

    /// <summary>True while the menu (or a submenu) is showing — the screen ORs this into the host
    /// keyboard system's <c>ShouldSuppressInput</c> (with <c>Dialog.IsOpen</c>) so Escape closes the
    /// menu.</summary>
    public bool IsOpen => _open;

    /// <summary>The open menu's top-level item list (empty when closed). Exposed for tests.</summary>
    public IReadOnlyList<EditorMenuItem> Items => _open ? _items : Array.Empty<EditorMenuItem>();

    /// <summary>The expanded submenu-parent item index, or -1. Exposed for tests.</summary>
    public int OpenSubmenuIndex => _openSubmenuIndex;

    /// <param name="isBlocked">Optional gate: while it returns <c>true</c> (the Save/confirm dialog is
    /// open, or a shell splitter/scrollbar drag owns the pointer) an <see cref="OpenAt"/> is refused —
    /// "if the dialog is open, menus never open". Null (the default) never blocks.</param>
    public EditorContextMenuSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        Action<string, GameState> dispatch,
        Func<KeyboardState>? getKeyboardState = null,
        Func<bool>? isBlocked = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
        _isBlocked = isBlocked;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    // ─── public API (openers, headless ops, tests) ───────────────────────────────────────────────

    /// <summary>Opens the menu <paramref name="items"/> at <paramref name="anchorScreen"/> (device
    /// pixels — a cursor position); the layout clamps it to the window.</summary>
    public void OpenAt(IReadOnlyList<EditorMenuItem> items, Point anchorScreen)
    {
        if (items == null || items.Count == 0) return;
        if (_isBlocked?.Invoke() == true) return; // dialog open / drag owns the pointer → menus never open
        EnsureBuilt();
        _items = items;
        _anchor = anchorScreen;
        _openSubmenuIndex = -1;
        _hoverMain = _hoverSub = -1;
        _prevKeys = _getKeyboardState(); // swallow the current key state so no stale edge fires
        _open = true;
    }

    /// <summary>Opens the menu <paramref name="items"/> anchored just BELOW a header button
    /// <paramref name="buttonBounds"/> (its bottom-left) — the <c>Entity ▾</c> dropdown.</summary>
    public void OpenBelow(IReadOnlyList<EditorMenuItem> items, Rectangle buttonBounds) =>
        OpenAt(items, new Point(buttonBounds.Left, buttonBounds.Bottom));

    /// <summary>Picks the leaf item with action-id <paramref name="path"/> from the OPEN menu (searching
    /// submenus) and dispatches it — the headless <c>menu:pick &lt;path&gt;</c> op. A disabled or missing
    /// item logs and leaves the menu open.</summary>
    public void Pick(string path, GameState state)
    {
        if (!_open)
        {
            Logger.Warning($"[level-editor] menu:pick '{path}': no menu is open.");
            return;
        }
        var item = EditorContextMenuModel.FindByPath(_items, path);
        if (item == null)
        {
            Logger.Warning($"[level-editor] menu:pick '{path}': the open menu has no such item.");
            return;
        }
        DispatchItem(item, state);
    }

    /// <summary>Closes the menu (item click / click-away / Escape / the <c>menu:close</c> op).</summary>
    public void Close()
    {
        _open = false;
        _openSubmenuIndex = -1;
    }

    // ─── per-frame ────────────────────────────────────────────────────────────────────────────────

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        if (!_open)
        {
            if (_built) ParkAll();
            return;
        }

        var scale = _viewportManager.DevicePixelRatio;
        ReadKeyboard(state);
        if (!_open) { ParkAll(); return; }
        HandleMouseAndConsume(state, scale);
        if (!_open) { ParkAll(); return; }
        Layout(scale);
    }

    /// <summary>Escape closes the menu (the keyboard half — the screen suppresses editor/game keys while
    /// the menu owns input).</summary>
    private void ReadKeyboard(GameState state)
    {
        var keys = _getKeyboardState();
        var escape = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        _prevKeys = keys;
        if (escape) Close();
    }

    private void HandleMouseAndConsume(GameState state, float scale)
    {
        _hoverMain = _hoverSub = -1;
        _leftDown = false;
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            _leftDown = input.LeftButton;

            var menu = EditorContextMenuLayout.MenuRect(_anchor, _items, w, h, scale);
            var overMain = HitItem(menu, _items, point, scale);

            // A submenu opens on hover of its parent; it stays open while the cursor is over it or its
            // parent, and closes when a DIFFERENT main item is hovered.
            if (overMain >= 0 && _items[overMain].Kind == EditorMenuItemKind.Submenu && _items[overMain].Enabled)
                _openSubmenuIndex = overMain;

            var overSub = -1;
            if (_openSubmenuIndex >= 0 && _items[_openSubmenuIndex].Submenu is { } subItems)
            {
                var parentRect = EditorContextMenuLayout.ItemRect(menu, _items, _openSubmenuIndex, scale);
                var subRect = EditorContextMenuLayout.SubmenuRect(menu, parentRect, subItems, w, h, scale);
                overSub = HitItem(subRect, subItems, point, scale);
                if (overSub < 0 && overMain >= 0 && overMain != _openSubmenuIndex)
                    _openSubmenuIndex = -1;
            }

            _hoverMain = overMain;
            _hoverSub = overSub;

            if (input.LeftButtonReleased)
            {
                if (overSub >= 0 && _items[_openSubmenuIndex].Submenu is { } sub)
                    DispatchItem(sub[overSub], state);
                else if (overMain >= 0)
                {
                    var item = _items[overMain];
                    if (item.Kind == EditorMenuItemKind.Action) DispatchItem(item, state);
                    // submenu parent / separator click: no dispatch (submenu stays open on its parent)
                }
                else
                {
                    Close(); // click-away closes WITHOUT acting
                }
            }

            ConsumeCursor(ref input);
            cursor.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    /// <summary>The index of the item under <paramref name="point"/> inside a menu box, or -1.
    /// Separators are never "hit" (they are non-interactive dividers).</summary>
    private static int HitItem(Rectangle box, IReadOnlyList<EditorMenuItem> items, Point point, float scale)
    {
        if (!box.Contains(point)) return -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == EditorMenuItemKind.Separator) continue;
            if (EditorContextMenuLayout.ItemRect(box, items, i, scale).Contains(point)) return i;
        }
        return -1;
    }

    /// <summary>Dispatches a leaf action item (enabled) and closes; a disabled item or a non-action is a
    /// no-op that keeps the menu open. Closes BEFORE dispatching so an action that opens another modal
    /// (Create Empty Scene → the dialog) lands cleanly.</summary>
    private void DispatchItem(EditorMenuItem item, GameState state)
    {
        if (item.Kind != EditorMenuItemKind.Action || !item.Enabled) return;
        var path = item.Path;
        Close();
        _dispatch(path, state);
    }

    /// <summary>Clears the cursor's pointer edges + button level fields for this frame (the modal
    /// consume) — the same recipe as the dialog. The menu acts on the release edge BEFORE this clears it;
    /// the edge survives here because <c>CursorInputSystem</c> derives edges from its own previous state,
    /// not these fields.</summary>
    private static void ConsumeCursor(ref CursorInputComponent input)
    {
        input.LeftButtonPressed = input.RightButtonPressed = input.MiddleButtonPressed = false;
        input.LeftButtonReleased = input.RightButtonReleased = input.MiddleButtonReleased = false;
        input.LeftButton = input.RightButton = input.MiddleButton = false;
        input.ScrollWheelDelta = 0;
    }

    // ─── layout + render ───────────────────────────────────────────────────────────────────────────

    private void Layout(float scale)
    {
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var menu = EditorContextMenuLayout.MenuRect(_anchor, _items, w, h, scale);

        PlaceBox(_menuBox, menu);
        EnsurePool(_mainPool, _items.Count);
        LayoutItems(_mainPool, _menuBox, menu, _items, _hoverMain, scale);

        // The open submenu (or park the sub chrome).
        if (_openSubmenuIndex >= 0 && _items[_openSubmenuIndex].Submenu is { } subItems)
        {
            var parentRect = EditorContextMenuLayout.ItemRect(menu, _items, _openSubmenuIndex, scale);
            var subRect = EditorContextMenuLayout.SubmenuRect(menu, parentRect, subItems, w, h, scale);
            PlaceBox(_subBox, subRect);
            EnsurePool(_subPool, subItems.Count);
            LayoutItems(_subPool, _subBox, subRect, subItems, _hoverSub, scale);
        }
        else
        {
            ParkBox(_subBox);
            foreach (var v in _subPool) ParkRow(v);
        }
    }

    private void LayoutItems(List<MenuRowVisual> pool, Entity box, Rectangle boxRect,
        IReadOnlyList<EditorMenuItem> items, int hovered, float scale)
    {
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        for (var i = 0; i < pool.Count; i++)
        {
            var v = pool[i];
            if (i >= items.Count) { ParkRow(v); continue; }

            var item = items[i];
            var rect = EditorContextMenuLayout.ItemRect(boxRect, items, i, scale);

            if (item.Kind == EditorMenuItemKind.Separator)
            {
                ParkBox(v.Fill); Park(v.Label); ClearMesh(v.Caret);
                var line = EditorContextMenuLayout.SeparatorLine(rect, scale);
                PlaceBox(v.Sep, line);
                SetBoxFill(v.Sep, EditorTheme.Border);
                continue;
            }

            ParkBox(v.Sep);

            // Hover fill (INSTANT — pooled rows never fade, pre-mortem #6): hovered enabled row = Bg3.
            if (i == hovered && item.Enabled)
            {
                PlaceBox(v.Fill, rect);
                SetBoxFill(v.Fill, EditorTheme.Bg3);
            }
            else
            {
                ParkBox(v.Fill);
            }

            var color = !item.Enabled ? EditorTheme.TextDisabled
                : item.Danger ? EditorTheme.Danger
                : EditorTheme.Text0;
            PlaceLabel(v.Label, EditorContextMenuLayout.ItemText(rect, labelHeight, scale), item.Label, color, scale);

            if (item.Kind == EditorMenuItemKind.Submenu)
            {
                var caretRect = EditorContextMenuLayout.CaretRect(rect, scale);
                var tri = SystemsPanelLayout.ArrowTriangle(caretRect, expanded: false); // ▸
                SetMesh(v.Caret, new FilledTriangleMeshGenerator(tri[0], tri[1], tri[2], color).Generate());
            }
            else
            {
                ClearMesh(v.Caret);
            }
        }
    }

    // ─── entity construction (chrome: Editor target, no VisibleComponent) ────────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        _menuBox = CreateBox(EditorTheme.Bg1, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.MenuPanel);
        _subBox = CreateBox(EditorTheme.Bg1, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.MenuPanel);
        _built = true;
        ParkAll();
    }

    private void EnsurePool(List<MenuRowVisual> pool, int count)
    {
        while (pool.Count < count)
            pool.Add(new MenuRowVisual
            {
                Fill = CreateBox(EditorTheme.Bg3, Color.Transparent, 0f, EditorTheme.Depths.MenuControl),
                Sep = CreateBox(EditorTheme.Border, Color.Transparent, 0f, EditorTheme.Depths.MenuControl),
                Label = CreateLabel(EditorTheme.Depths.MenuLabel),
                Caret = CreateMesh(EditorTheme.Depths.MenuLabel),
            });
    }

    private Entity CreateBox(Color fill, Color outline, float thickness, float depth)
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        e.Set(new TransformComponent(ParkPosition));
        e.Set(new SimpleButtonComponent
        {
            Size = Vector2.One,
            LineThickness = thickness,
            Color = outline,
            FillColor = fill,
            Target = RenderTargetID.Editor,
            LayerDepth = depth,
        });
        // NOTE: no VisibleComponent and no ToolbarButtonComponent (chrome rule; the menu owns its own
        // hit-test — ToolbarSystem must not see these).
        return e;
    }

    private Entity CreateLabel(float depth)
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new TransformComponent(ParkPosition));
        e.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = depth,
            TextContent = string.Empty,
            Font = _font!,
            Color = EditorTheme.Text0,
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
        e.Set(new TransformComponent(ParkPosition));
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = depth,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
        return e;
    }

    // ─── placement helpers ───────────────────────────────────────────────────────────────────────

    private static void PlaceBox(Entity e, Rectangle rect)
    {
        Place(e, new Vector2(rect.X, rect.Y));
        ref var visual = ref e.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
    }

    private static void SetBoxFill(Entity e, Color fill)
    {
        if (e.IsAlive) e.Get<SimpleButtonComponent>().FillColor = fill;
    }

    private void PlaceLabel(Entity e, Vector2 position, string text, Color color, float scale)
    {
        Place(e, position);
        ref var display = ref e.Get<DynamicTextComponent>();
        display.TextContent = text;
        display.Color = color;
        display.Scale = EditorChromeBuilder.LabelScale * scale;
    }

    private static void SetMesh(Entity e, MeshData mesh)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.Target = RenderTargetID.Editor;
    }

    private static void ClearMesh(Entity e)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Vertices = Array.Empty<VertexPositionColor>();
        dc.Indices = Array.Empty<int>();
    }

    private static void Place(Entity e, Vector2 position)
    {
        ref var transform = ref e.Get<TransformComponent>();
        transform.Position = position;
        e.NotifyChanged<TransformComponent>();
    }

    private void ParkAll()
    {
        ParkBox(_menuBox);
        ParkBox(_subBox);
        foreach (var v in _mainPool) ParkRow(v);
        foreach (var v in _subPool) ParkRow(v);
    }

    private static void ParkRow(MenuRowVisual v)
    {
        ParkBox(v.Fill);
        ParkBox(v.Sep);
        Park(v.Label);
        ClearMesh(v.Caret);
    }

    private static void ParkBox(Entity e)
    {
        if (!e.IsAlive) return;
        Place(e, ParkPosition);
        e.Get<SimpleButtonComponent>().Size = Vector2.Zero;
    }

    private static void Park(Entity e)
    {
        if (e.IsAlive) Place(e, ParkPosition);
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One pooled item row's visuals (repurposed per open): a hover-fill box, a separator line
    /// box, a label, and a submenu ▸ caret mesh. Parked (off-screen / emptied) when unused.</summary>
    private sealed class MenuRowVisual
    {
        public Entity Fill;
        public Entity Sep;
        public Entity Label;
        public Entity Caret;
    }
}
