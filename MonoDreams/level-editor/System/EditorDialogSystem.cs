#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>Which modal dialog the editor is showing (if any).</summary>
public enum EditorDialogMode
{
    /// <summary>No dialog is open.</summary>
    None,
    /// <summary>The Save dialog: a name field + Save / Cancel.</summary>
    Save,
    /// <summary>The Load dialog: a scrollable list of existing scenes + Cancel.</summary>
    Load,
}

/// <summary>The Load dialog's source data: whether the project root resolved, the scene ids to
/// list, and (when unresolved / empty) an actionable message to show instead of an empty list.</summary>
public readonly record struct SceneListing(bool Resolved, IReadOnlyList<string> SceneIds, string? Message);

/// <summary>
/// The editor's modal <b>Save / Load dialogs</b> — native-resolution chrome on
/// <c>RenderTargetID.Editor</c> (screen-space, DPR-scaled), built from the same engine primitives
/// as the rest of the shell (<see cref="SimpleButtonComponent"/> fills/outlines prepped by the
/// woven <c>ButtonMeshPrepSystem</c>, <see cref="DynamicTextComponent"/> labels), laid out by the
/// pure <see cref="EditorDialogLayout"/> and hit-tested against the cursor's raw
/// <see cref="CursorInputComponent.ScreenPosition"/> — never <c>VirtualPosition</c>, which is
/// frozen over the chrome margins. Per the chrome rule the dialog entities carry <b>no</b>
/// <c>VisibleComponent</c>; they are shown/hidden by parking off-screen (the SystemsPanel idiom),
/// not by toggling visibility.
///
/// <para><b>Why editor-native, not the <c>ui</c> <c>DialogComponent</c>.</b> The <c>ui</c> module's
/// <c>DialogSystem</c> toggles <c>VisibleComponent</c> and is modal via <c>UIFocusSystem</c> focus
/// groups — both are Main/HUD-target mechanisms that the editor's native-resolution Editor-target
/// chrome deliberately does <b>not</b> use (adding <c>VisibleComponent</c> to Editor chrome would
/// pull it into <c>MeshPrepSystem</c> and double-offset the pre-baked meshes — see
/// <c>EditorChromeBuilder</c>). Reusing it would break the chrome invariant, so the dialog is built
/// the same way <see cref="SystemsPanelSystem"/> is (its sibling chrome widget), and it reuses the
/// <c>ui</c> primitives it can (<see cref="SimpleButtonComponent"/> / <see cref="DynamicTextComponent"/>)
/// rather than inventing new draw components.</para>
///
/// <para><b>Modality (the dialog owns input while open).</b> Two facets: (1) <b>mouse</b> — after
/// hit-testing its own buttons/rows, the system <b>consumes the cursor's pointer edges</b> (clears
/// the press/release/scroll edges on the single cursor entity), so every mouse-driven editor system
/// downstream this frame (toolbar, selection, gizmo, camera-nav, palette, boundary, systems-panel)
/// sees no click and does not act — no per-system edit needed. (2) <b>keyboard</b> — the composing
/// screen wires the host keyboard system's <c>ShouldSuppressInput</c> to <see cref="IsOpen"/>, so
/// every editor/game keyboard action (delete, undo/redo, frame, boundary-commit, and crucially the
/// game's Escape-to-exit) stands down while the dialog reads the keyboard for its own field. Escape
/// cancels; Enter confirms (Save).</para>
///
/// <para><b>Headless-drivable.</b> Every action has a public method (<see cref="OpenSave"/> /
/// <see cref="OpenLoad"/> / <see cref="SetName"/> / <see cref="Confirm"/> / <see cref="Cancel"/> /
/// <see cref="SelectLoad"/>) so the <c>dialog:*</c> editor-op grammar drives the full flow with no
/// real keyboard/mouse — see <c>EditorOverlay.DispatchNamedAction</c>.</para>
///
/// <para><b>Game-agnostic.</b> It knows nothing of <c>SceneWriter</c> / project paths: the confirm /
/// select outcomes fire callbacks the overlay supplies (which run the SAME guarded Save / Load
/// paths the toolbar used), and the Load list comes from an injected <see cref="SceneListing"/>
/// provider — so the module never touches the filesystem and the whole flow is unit-testable
/// in-process with no GraphicsDevice.</para>
/// </summary>
public sealed class EditorDialogSystem : ISystem<GameState>
{
    private static readonly Color BackdropColor = new(10, 10, 14);
    private static readonly Color PanelColor = new(30, 30, 36);

    // Dialog depths sit ABOVE the shell chrome (panels 0.1 / buttons 0.5 / labels 0.6) so the modal
    // covers the toolbar + systems panel.
    private const float BackdropDepth = 0.70f;
    private const float PanelDepth = 0.74f;
    private const float ControlDepth = 0.80f;
    private const float LabelDepth = 0.86f;

    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Action<string, GameState> _onSaveConfirmed;
    private readonly Action<string, GameState> _onLoadSelected;
    private readonly Func<SceneListing> _listScenes;
    private readonly Func<KeyboardState> _getKeyboardState;
    private readonly EntitySet _cursorSet;

    private readonly EditorTextField _field = new();
    private EditorDialogMode _mode = EditorDialogMode.None;
    private SceneListing _listing;
    private int _loadScroll;
    private KeyboardState _prevKeys;

    private bool _built;
    private Entity _backdrop, _panel, _title, _fieldBox, _fieldText, _confirmBox, _confirmLabel,
        _cancelBox, _cancelLabel, _message;
    private readonly List<(Entity box, Entity label)> _rows = new();

    public bool IsEnabled { get; set; } = true;

    /// <summary>True while any dialog is open — the screen wires this to the host keyboard system's
    /// <c>ShouldSuppressInput</c> so editor/game keys stand down while the dialog owns input.</summary>
    public bool IsOpen => _mode != EditorDialogMode.None;

    /// <summary>The open dialog kind (or <see cref="EditorDialogMode.None"/>). Exposed for tests.</summary>
    public EditorDialogMode Mode => _mode;

    /// <summary>The current Save-name field value (raw, pre-sanitize). Exposed for tests.</summary>
    public string NameValue => _field.Value;

    /// <summary>The scene ids the Load dialog is currently listing. Exposed for tests.</summary>
    public IReadOnlyList<string> ListedSceneIds => _listing.SceneIds ?? Array.Empty<string>();

    public EditorDialogSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        Action<string, GameState> onSaveConfirmed,
        Action<string, GameState> onLoadSelected,
        Func<SceneListing> listScenes,
        Func<KeyboardState>? getKeyboardState = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _onSaveConfirmed = onSaveConfirmed ?? throw new ArgumentNullException(nameof(onSaveConfirmed));
        _onLoadSelected = onLoadSelected ?? throw new ArgumentNullException(nameof(onLoadSelected));
        _listScenes = listScenes ?? throw new ArgumentNullException(nameof(listScenes));
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    // ─── public API (toolbar dispatch, headless ops, tests) ──────────────────────────────────────

    /// <summary>Opens the Save dialog with the field prefilled to <paramref name="defaultName"/>
    /// (the current scene id).</summary>
    public void OpenSave(string defaultName)
    {
        EnsureBuilt();
        _field.Set(defaultName);
        _prevKeys = _getKeyboardState(); // swallow the current key state so no stale edge fires
        _mode = EditorDialogMode.Save;
    }

    /// <summary>Opens the Load dialog and snapshots the current scene listing (files present, or the
    /// unresolved-project message).</summary>
    public void OpenLoad()
    {
        EnsureBuilt();
        _listing = _listScenes();
        _loadScroll = 0;
        _prevKeys = _getKeyboardState();
        _mode = EditorDialogMode.Load;
    }

    /// <summary>Replaces the Save-name field value (the headless <c>dialog:name</c> op).</summary>
    public void SetName(string text) => _field.Set(text);

    /// <summary>Confirms the current dialog. For Save: sanitizes the field to a safe file id and,
    /// when non-empty, fires the save callback and closes; an empty result keeps the dialog open and
    /// logs. (Load performs its action via <see cref="SelectLoad"/>.)</summary>
    public void Confirm(GameState state)
    {
        if (_mode != EditorDialogMode.Save) return;
        var id = EditorTextField.Sanitize(_field.Value);
        if (string.IsNullOrEmpty(id))
        {
            Logger.Warning(
                "[level-editor] Save dialog: the name is empty after reducing it to a safe file id " +
                "(letters, digits, '-' and '_'). Type a valid name.");
            return; // keep the dialog open
        }
        _onSaveConfirmed(id, state);
        Close();
    }

    /// <summary>Loads a scene by id (a Load-dialog row click or the headless <c>dialog:load</c> op)
    /// and closes.</summary>
    public void SelectLoad(string id, GameState state)
    {
        _onLoadSelected(id, state);
        Close();
    }

    /// <summary>Closes any open dialog (Escape / Cancel / the headless <c>dialog:cancel</c> op).</summary>
    public void Cancel() => Close();

    private void Close() => _mode = EditorDialogMode.None;

    // ─── per-frame ───────────────────────────────────────────────────────────────────────────────

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        if (_mode == EditorDialogMode.None)
        {
            if (_built) ParkAll();
            return;
        }

        var scale = _viewportManager.DevicePixelRatio;

        if (_mode == EditorDialogMode.Save) ReadSaveKeyboard(state);
        else ReadLoadKeyboard();

        HandleMouseAndConsume(state, scale);

        if (_mode == EditorDialogMode.None) { ParkAll(); return; }

        LayoutAndRender(state, scale);
    }

    private void ReadSaveKeyboard(GameState state)
    {
        var keys = _getKeyboardState();
        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key)) continue; // only newly-pressed this frame
            switch (key)
            {
                case Keys.Back: _field.Backspace(); continue;
                case Keys.Enter: _prevKeys = keys; Confirm(state); return;
                case Keys.Escape: _prevKeys = keys; Cancel(); return;
            }
            var c = KeyToChar(key);
            if (c != '\0') _field.Append(c);
        }
        _prevKeys = keys;
    }

    private void ReadLoadKeyboard()
    {
        var keys = _getKeyboardState();
        var escapePressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        _prevKeys = keys;
        if (escapePressed) Cancel();
    }

    /// <summary>Hit-tests the dialog's controls against the cursor's native <c>ScreenPosition</c>,
    /// then <b>consumes</b> the cursor's pointer edges so no editor system downstream acts on the
    /// same click/scroll this frame (the mouse half of the modal capture).</summary>
    private void HandleMouseAndConsume(GameState state, float scale)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            var panel = EditorDialogLayout.Panel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight,
                _mode == EditorDialogMode.Load, scale);

            if (_mode == EditorDialogMode.Load && input.ScrollWheelDelta != 0 && panel.Contains(point))
                ScrollLoadList(panel, scale, input.ScrollWheelDelta);

            if (input.LeftButtonReleased)
            {
                if (_mode == EditorDialogMode.Save)
                {
                    if (EditorDialogLayout.ConfirmButton(panel, scale).Contains(point)) Confirm(state);
                    else if (EditorDialogLayout.CancelButton(panel, false, scale).Contains(point)) Cancel();
                }
                else // Load
                {
                    if (EditorDialogLayout.CancelButton(panel, true, scale).Contains(point)) Cancel();
                    else HitTestLoadRow(panel, scale, point, state);
                }
            }

            // Consume the pointer for this frame (modal): downstream mouse systems see no edges.
            input.LeftButtonPressed = input.RightButtonPressed = input.MiddleButtonPressed = false;
            input.LeftButtonReleased = input.RightButtonReleased = input.MiddleButtonReleased = false;
            input.LeftButton = input.RightButton = input.MiddleButton = false;
            input.ScrollWheelDelta = 0;
            cursor.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    private void HitTestLoadRow(Rectangle panel, float scale, Point point, GameState state)
    {
        var ids = _listing.SceneIds;
        if (ids == null || ids.Count == 0) return;
        var visible = EditorDialogLayout.VisibleRowCount(panel, scale);
        for (var vi = 0; vi < visible; vi++)
        {
            var i = _loadScroll + vi;
            if (i >= ids.Count) break;
            if (EditorDialogLayout.Row(panel, vi, scale).Contains(point))
            {
                SelectLoad(ids[i], state);
                return;
            }
        }
    }

    private void ScrollLoadList(Rectangle panel, float scale, int wheelDelta)
    {
        var count = _listing.SceneIds?.Count ?? 0;
        var visible = EditorDialogLayout.VisibleRowCount(panel, scale);
        var max = Math.Max(0, count - visible);
        _loadScroll = Math.Clamp(_loadScroll + (wheelDelta > 0 ? -1 : 1), 0, max);
    }

    // ─── layout + render ───────────────────────────────────────────────────────────────────────

    private void LayoutAndRender(GameState state, float scale)
    {
        var isLoad = _mode == EditorDialogMode.Load;
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var panel = EditorDialogLayout.Panel(w, h, isLoad, scale);

        PlaceBox(_backdrop, EditorDialogLayout.Backdrop(w, h));
        PlaceBox(_panel, panel);
        PlaceLabel(_title, EditorDialogLayout.Title(panel, scale), isLoad ? "Load Scene" : "Save Scene",
            EditorChromeBuilder.LabelColor, scale);

        // Cancel is always present (Load's only button; Save's second button).
        var cancel = EditorDialogLayout.CancelButton(panel, isLoad, scale);
        PlaceBox(_cancelBox, cancel);
        PlaceLabel(_cancelLabel, LabelInset(cancel, scale), "Cancel", EditorChromeBuilder.LabelColor, scale);

        if (isLoad)
        {
            ParkBox(_confirmBox); Park(_confirmLabel);
            ParkBox(_fieldBox); Park(_fieldText);
            RenderLoadList(panel, scale);
        }
        else
        {
            ParkMessage();
            ParkRows();

            var confirm = EditorDialogLayout.ConfirmButton(panel, scale);
            PlaceBox(_confirmBox, confirm);
            PlaceLabel(_confirmLabel, LabelInset(confirm, scale), "Save", EditorChromeBuilder.LabelColor, scale);

            var field = EditorDialogLayout.Field(panel, scale);
            PlaceBox(_fieldBox, field);
            var caretOn = (state.TotalTime % 1.0) < 0.5;
            var shown = _field.Value + (caretOn ? "|" : string.Empty);
            PlaceLabel(_fieldText, EditorDialogLayout.FieldText(field, scale), shown,
                EditorChromeBuilder.LabelColor, scale);
        }
    }

    private void RenderLoadList(Rectangle panel, float scale)
    {
        var ids = _listing.SceneIds ?? Array.Empty<string>();
        var showMessage = !_listing.Resolved || ids.Count == 0;
        if (showMessage)
        {
            var text = _listing.Message
                ?? (!_listing.Resolved ? "No project root resolved." : "No saved scenes yet.");
            PlaceLabel(_message, EditorDialogLayout.Message(panel, scale), text,
                EditorChromeBuilder.LabelColor, scale);
            ParkRows();
            return;
        }

        ParkMessage();
        var visible = EditorDialogLayout.VisibleRowCount(panel, scale);
        _loadScroll = Math.Clamp(_loadScroll, 0, Math.Max(0, ids.Count - visible));

        for (var vi = 0; vi < visible; vi++)
        {
            var i = _loadScroll + vi;
            EnsureRow(vi);
            var (box, label) = _rows[vi];
            if (i >= ids.Count) { ParkBox(box); Park(label); continue; }
            var rect = EditorDialogLayout.Row(panel, vi, scale);
            PlaceBox(box, rect);
            PlaceLabel(label, LabelInset(rect, scale), ids[i], EditorChromeBuilder.LabelColor, scale);
        }
        for (var vi = visible; vi < _rows.Count; vi++)
        {
            ParkBox(_rows[vi].box);
            Park(_rows[vi].label);
        }
    }

    private Vector2 LabelInset(Rectangle rect, float scale)
    {
        var labelH = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        return new Vector2(rect.X + EditorDialogLayout.Px(8, scale), rect.Y + (rect.Height - labelH) / 2f);
    }

    // ─── entity construction (chrome: Editor target, no VisibleComponent) ────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        _backdrop = CreateBox(BackdropColor, BackdropColor, 0f, BackdropDepth);
        _panel = CreateBox(PanelColor, EditorChromeBuilder.ButtonOutline, 1.5f, PanelDepth);
        _title = CreateLabel(LabelDepth);
        _fieldBox = CreateBox(EditorChromeBuilder.ButtonFill, EditorChromeBuilder.ButtonOutline, 1.5f, ControlDepth);
        _fieldText = CreateLabel(LabelDepth);
        _confirmBox = CreateBox(EditorChromeBuilder.ButtonFill, EditorChromeBuilder.ButtonOutline, 1.5f, ControlDepth);
        _confirmLabel = CreateLabel(LabelDepth);
        _cancelBox = CreateBox(EditorChromeBuilder.ButtonFill, EditorChromeBuilder.ButtonOutline, 1.5f, ControlDepth);
        _cancelLabel = CreateLabel(LabelDepth);
        _message = CreateLabel(LabelDepth);
        _built = true;
        ParkAll();
    }

    private void EnsureRow(int index)
    {
        while (_rows.Count <= index)
        {
            var box = CreateBox(EditorChromeBuilder.ButtonFill, EditorChromeBuilder.ButtonOutline, 1f, ControlDepth);
            var label = CreateLabel(LabelDepth);
            _rows.Add((box, label));
        }
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
        // NOTE: no VisibleComponent and no ToolbarButtonComponent (chrome rule; the dialog owns its
        // own hit-test — ToolbarSystem must not see these).
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
            Color = EditorChromeBuilder.LabelColor,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
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

    private void PlaceLabel(Entity e, Vector2 position, string text, Color color, float scale)
    {
        Place(e, position);
        ref var display = ref e.Get<DynamicTextComponent>();
        display.TextContent = text;
        display.Color = color;
        display.Scale = EditorChromeBuilder.LabelScale * scale;
    }

    private static void Place(Entity e, Vector2 position)
    {
        ref var transform = ref e.Get<TransformComponent>();
        transform.Position = position;
        e.NotifyChanged<TransformComponent>();
    }

    private void ParkAll()
    {
        ParkBox(_backdrop); ParkBox(_panel); Park(_title);
        ParkBox(_fieldBox); Park(_fieldText);
        ParkBox(_confirmBox); Park(_confirmLabel);
        ParkBox(_cancelBox); Park(_cancelLabel);
        ParkMessage();
        ParkRows();
    }

    private void ParkRows()
    {
        foreach (var (box, label) in _rows) { ParkBox(box); Park(label); }
    }

    private void ParkMessage() => Park(_message);

    private static void ParkBox(Entity e)
    {
        if (!e.IsAlive) return;
        Place(e, ParkPosition);
        ref var visual = ref e.Get<SimpleButtonComponent>();
        visual.Size = Vector2.Zero;
    }

    private static void Park(Entity e)
    {
        if (e.IsAlive) Place(e, ParkPosition);
    }

    /// <summary>Poll-based key → char, mirroring <c>ui.TextInputSystem</c> (lowercase, no shift):
    /// letters, digits, and '-'. Everything else is dropped here; <see cref="EditorTextField.Sanitize"/>
    /// is the final gate on confirm.</summary>
    private static char KeyToChar(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
        >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
        >= Keys.A and <= Keys.Z => (char)('a' + (key - Keys.A)),
        Keys.OemMinus or Keys.Subtract => '-',
        _ => '\0',
    };

    public void Dispose()
    {
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
