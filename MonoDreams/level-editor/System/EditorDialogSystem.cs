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
    /// <summary>The Save browser: navigate to a folder, type a name, Save / Cancel.</summary>
    Save,
    /// <summary>The Load browser: navigate to a folder, pick a <c>.mdscene</c> file / Cancel.</summary>
    Load,
}

/// <summary>
/// The editor's modal <b>Save / Load file-system navigator</b> — a Blender-style directory browser,
/// native-resolution chrome on <c>RenderTargetID.Editor</c> (screen-space, DPR-scaled), built from the
/// same engine primitives as the rest of the shell (<see cref="SimpleButtonComponent"/> fills/outlines
/// prepped by the woven <c>ButtonMeshPrepSystem</c>, <see cref="DynamicTextComponent"/> labels), laid
/// out by the pure <see cref="EditorDialogLayout"/> and hit-tested against the cursor's raw
/// <see cref="CursorInputComponent.ScreenPosition"/> — never <c>VirtualPosition</c>, which is frozen
/// over the chrome margins. Per the chrome rule the dialog entities carry <b>no</b>
/// <c>VisibleComponent</c>; they are shown/hidden by parking off-screen (the SystemsPanel idiom).
///
/// <para><b>The browser.</b> The pure <see cref="EditorFileBrowser"/> owns navigation: it lists a
/// directory's subfolders + <c>.mdscene</c> files, descends (<see cref="EnterDirectory"/>) and climbs
/// (<see cref="GoUp"/>) <b>bounded at the project root</b>, and opens at the project's scenes dir
/// (<c>LevelsPath</c>). It is NOT a free OS file picker: it never escapes the project root, and scenes
/// live under <c>Content/Levels</c> so they bundle + load per the persistence design (see the
/// browser's own doc and the level-editor premises). The dialog renders folder rows as
/// <c>name/</c> and scene rows as the id; a click on a folder descends, a click on a file <b>loads</b>
/// it (Load) or fills the filename field to overwrite it (Save).</para>
///
/// <para><b>Modality (the dialog owns input while open).</b> Two facets: (1) <b>mouse</b> — after
/// hit-testing its own controls, the system <b>consumes the cursor's pointer edges</b> (clears the
/// press/release/scroll edges on the single cursor entity), so every mouse-driven editor system
/// downstream this frame sees no click and does not act — no per-system edit needed. (2)
/// <b>keyboard</b> — the composing screen wires the host keyboard system's <c>ShouldSuppressInput</c>
/// to <see cref="IsOpen"/>, so every editor/game key (delete, undo/redo, the game's Escape-to-exit)
/// stands down while the dialog reads the keyboard for its own field. Escape cancels; Enter confirms
/// (Save). The release-edge action survives its own consume only because the cursor derives its edges
/// from its OWN previous-state (the EF1 cursor premise), not from the level fields the dialog clears.</para>
///
/// <para><b>Headless-drivable.</b> Every action has a public method (<see cref="OpenSave"/> /
/// <see cref="OpenLoad"/> / <see cref="SetName"/> / <see cref="Confirm"/> / <see cref="Cancel"/> /
/// <see cref="EnterDirectory"/> / <see cref="GoUp"/> / <see cref="PickFile"/>) so the <c>dialog:*</c>
/// editor-op grammar drives the full flow with no real keyboard/mouse — see
/// <c>EditorOverlay.DispatchNamedAction</c> (<c>save-open</c> / <c>load-open</c> / <c>name</c> /
/// <c>confirm</c> / <c>cancel</c> / <c>cd</c> / <c>up</c> / <c>pick</c>).</para>
///
/// <para><b>Game-agnostic.</b> It knows nothing of <c>SceneWriter</c> / project paths: the confirm /
/// pick outcomes fire callbacks the overlay supplies with the resolved <b>absolute file path</b>
/// (which run the SAME guarded Save / Load paths the toolbar used), and the browsable tree comes from
/// injected providers (<see cref="BrowserRoots"/> + a <c>listDirectory</c> function) — so the module
/// never touches the filesystem and the whole flow is unit-testable in-process with no GraphicsDevice.
/// </para>
/// </summary>
public sealed class EditorDialogSystem : ISystem<GameState>
{
    // Colors + depths come from EditorTheme (the module's single source): the dialog band sits ABOVE
    // the shell chrome (panels 0.1 / buttons 0.5 / labels 0.6) so the modal covers the toolbar + panel.

    // Per-widget hover-fade progress for the three persistent dialog buttons (Confirm / Cancel / Up) —
    // stored on the system (the buttons are parked persistent entities, so their fade state lives
    // alongside, never keyed to a pooled row — pre-mortem #6). Advanced framerate-independently.
    private float _confirmHover, _cancelHover, _upHover;
    private int _hoverControl = -1; // 0=confirm, 1=cancel, 2=up, else a visible row index + 3, or -1
    private bool _leftDown;

    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Action<string, GameState> _onSaveConfirmed;
    private readonly Action<string, GameState> _onLoadSelected;
    private readonly Func<BrowserRoots> _getRoots;
    private readonly Func<KeyboardState> _getKeyboardState;
    private readonly EntitySet _cursorSet;

    private readonly EditorTextField _field = new();
    private readonly EditorFileBrowser _browser;
    private EditorDialogMode _mode = EditorDialogMode.None;
    private int _scroll;
    private KeyboardState _prevKeys;

    private bool _built;
    private Entity _backdrop, _panel, _title, _breadcrumb, _upBox, _upLabel, _fieldBox, _fieldText,
        _confirmBox, _confirmLabel, _cancelBox, _cancelLabel, _message;
    private readonly List<(Entity box, Entity label)> _rows = new();

    public bool IsEnabled { get; set; } = true;

    /// <summary>True while any dialog is open — the screen wires this to the host keyboard system's
    /// <c>ShouldSuppressInput</c> so editor/game keys stand down while the dialog owns input.</summary>
    public bool IsOpen => _mode != EditorDialogMode.None;

    /// <summary>The open dialog kind (or <see cref="EditorDialogMode.None"/>). Exposed for tests.</summary>
    public EditorDialogMode Mode => _mode;

    /// <summary>The current Save-name field value (raw, pre-sanitize). Exposed for tests.</summary>
    public string NameValue => _field.Value;

    /// <summary>The browser's current directory (null when unresolved). Exposed for tests.</summary>
    public string? CurrentDirectory => _browser.CurrentDir;

    /// <summary>The subfolders in the current directory. Exposed for tests.</summary>
    public IReadOnlyList<string> Directories => _browser.Directories;

    /// <summary>The <c>.mdscene</c> ids in the current directory. Exposed for tests.</summary>
    public IReadOnlyList<string> Files => _browser.Files;

    /// <summary>Whether the up-directory control would move (false at the project-root boundary).</summary>
    public bool CanGoUp => _browser.CanGoUp;

    /// <summary>The breadcrumb path display string. Exposed for tests.</summary>
    public string BreadcrumbText => _browser.BreadcrumbText;

    public EditorDialogSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        Action<string, GameState> onSaveConfirmed,
        Action<string, GameState> onLoadSelected,
        Func<BrowserRoots> getRoots,
        Func<string, RawDirectory> listDirectory,
        Func<KeyboardState>? getKeyboardState = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _onSaveConfirmed = onSaveConfirmed ?? throw new ArgumentNullException(nameof(onSaveConfirmed));
        _onLoadSelected = onLoadSelected ?? throw new ArgumentNullException(nameof(onLoadSelected));
        _getRoots = getRoots ?? throw new ArgumentNullException(nameof(getRoots));
        _browser = new EditorFileBrowser(listDirectory ?? throw new ArgumentNullException(nameof(listDirectory)));
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    // ─── public API (toolbar dispatch, headless ops, tests) ──────────────────────────────────────

    /// <summary>Opens the Save browser rooted at the project scenes dir, with the field prefilled to
    /// <paramref name="defaultName"/> (the current scene id).</summary>
    public void OpenSave(string defaultName)
    {
        EnsureBuilt();
        _field.Set(defaultName);
        _browser.Open(_getRoots());
        _scroll = 0;
        _prevKeys = _getKeyboardState(); // swallow the current key state so no stale edge fires
        _mode = EditorDialogMode.Save;
    }

    /// <summary>Opens the Load browser rooted at the project scenes dir.</summary>
    public void OpenLoad()
    {
        EnsureBuilt();
        _browser.Open(_getRoots());
        _scroll = 0;
        _prevKeys = _getKeyboardState();
        _mode = EditorDialogMode.Load;
    }

    /// <summary>Replaces the Save-name field value (the headless <c>dialog:name</c> op).</summary>
    public void SetName(string text) => _field.Set(text);

    /// <summary>Descends into a listed subfolder (the headless <c>dialog:cd &lt;name&gt;</c> op / a
    /// folder-row click).</summary>
    public void EnterDirectory(string name)
    {
        if (_browser.Enter(name)) _scroll = 0;
    }

    /// <summary>Climbs to the parent directory, bounded at the project root (the headless
    /// <c>dialog:up</c> op / the up-button click).</summary>
    public void GoUp()
    {
        _browser.Up();
        _scroll = 0;
    }

    /// <summary>Picks a scene file by id in the current directory (the headless <c>dialog:pick</c> /
    /// <c>dialog:load</c> op / a file-row click). In Load mode it fires the load callback (with the
    /// resolved absolute path) and closes; in Save mode it fills the filename field so the user can
    /// overwrite that file.</summary>
    public void PickFile(string id, GameState state)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (_mode == EditorDialogMode.Save) { _field.Set(id); return; }
        var path = _browser.FilePath(id);
        if (string.IsNullOrEmpty(path))
        {
            Logger.Warning("[level-editor] Load: no project root resolved, so there is nothing to load.");
            return;
        }
        _onLoadSelected(path!, state);
        Close();
    }

    /// <summary>Confirms the Save dialog: sanitizes the field to a safe file id and, when non-empty,
    /// resolves it to <c>&lt;current-dir&gt;/&lt;id&gt;.mdscene</c>, fires the save callback and closes;
    /// an empty result (or an unresolved root) keeps the dialog open and logs. (Load acts via
    /// <see cref="PickFile"/>.)</summary>
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
        var path = _browser.FilePath(id);
        if (string.IsNullOrEmpty(path))
        {
            Logger.Warning(
                "[level-editor] Save dialog: no project root resolved, so there is nowhere to write. " +
                "Set MONODREAMS_PROJECT_ROOT in the run configuration.");
            return; // keep the dialog open
        }
        _onSaveConfirmed(path!, state);
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
        _hoverControl = -1;
        _leftDown = false;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            var isSave = _mode == EditorDialogMode.Save;
            var panel = EditorDialogLayout.Panel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight,
                !isSave, scale);

            // Per-frame hover for the interaction states (the fills apply it in LayoutAndRender/RenderList):
            // which control the cursor is over, and whether the left button is held (the pressed fill).
            _leftDown = input.LeftButton;
            _hoverControl = ComputeHoverControl(panel, isSave, scale, point);

            if (input.ScrollWheelDelta != 0 && panel.Contains(point))
                ScrollList(panel, isSave, scale, input.ScrollWheelDelta);

            if (input.LeftButtonReleased)
            {
                if (EditorDialogLayout.CancelButton(panel, !isSave, scale).Contains(point))
                {
                    Cancel();
                }
                else if (isSave && EditorDialogLayout.ConfirmButton(panel, scale).Contains(point))
                {
                    Confirm(state);
                }
                else if (_browser.Resolved && EditorDialogLayout.UpButton(panel, scale).Contains(point))
                {
                    GoUp();
                }
                else if (_browser.Resolved)
                {
                    HitTestRow(panel, isSave, scale, point, state);
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

    /// <summary>Which control the cursor is over, for the interaction-state fills: 0 = Confirm,
    /// 1 = Cancel, 2 = Up, <c>3 + visibleRowIndex</c> = a list row, or -1 = none. Mirrors the
    /// release hit-test order so hover and click agree.</summary>
    private int ComputeHoverControl(Rectangle panel, bool isSave, float scale, Point point)
    {
        if (EditorDialogLayout.CancelButton(panel, !isSave, scale).Contains(point)) return 1;
        if (isSave && EditorDialogLayout.ConfirmButton(panel, scale).Contains(point)) return 0;
        if (_browser.Resolved && EditorDialogLayout.UpButton(panel, scale).Contains(point)) return 2;
        if (_browser.Resolved)
        {
            var count = _browser.EntryCount;
            var visible = EditorDialogLayout.VisibleRowCount(panel, isSave, scale);
            for (var vi = 0; vi < visible; vi++)
            {
                var i = _scroll + vi;
                if (i >= count) break;
                if (EditorDialogLayout.Row(panel, vi, scale).Contains(point)) return 3 + vi;
            }
        }
        return -1;
    }

    /// <summary>A dialog button's fill: eases its own hover progress (persistent buttons store it on
    /// the system, never a pooled row — pre-mortem #6) and maps state → fill via the shared recipe.</summary>
    private Color DialogButtonFill(ref float hover, int controlId, bool disabled, float dt)
    {
        var hovered = _hoverControl == controlId;
        hover = EditorTheme.AdvanceHover(hover, hovered && !disabled, dt);
        return EditorTheme.ControlFill(disabled, selected: false, pressed: hovered && _leftDown && !disabled, hover);
    }

    private static void SetBoxFill(Entity e, Color fill)
    {
        if (!e.IsAlive) return;
        e.Get<SimpleButtonComponent>().FillColor = fill;
    }

    /// <summary>Maps a clicked visible row to a folder (descend) or a scene file (Load: load / Save:
    /// fill the name), folders listed before files.</summary>
    private void HitTestRow(Rectangle panel, bool isSave, float scale, Point point, GameState state)
    {
        var count = _browser.EntryCount;
        if (count == 0) return;
        var visible = EditorDialogLayout.VisibleRowCount(panel, isSave, scale);
        for (var vi = 0; vi < visible; vi++)
        {
            var i = _scroll + vi;
            if (i >= count) break;
            if (!EditorDialogLayout.Row(panel, vi, scale).Contains(point)) continue;
            if (_browser.IsDirectory(i))
                EnterDirectory(_browser.Directories[i]);
            else
                PickFile(_browser.Files[i - _browser.Directories.Count], state);
            return;
        }
    }

    private void ScrollList(Rectangle panel, bool isSave, float scale, int wheelDelta)
    {
        var visible = EditorDialogLayout.VisibleRowCount(panel, isSave, scale);
        var max = Math.Max(0, _browser.EntryCount - visible);
        _scroll = Math.Clamp(_scroll + (wheelDelta > 0 ? -1 : 1), 0, max);
    }

    // ─── layout + render ───────────────────────────────────────────────────────────────────────

    private void LayoutAndRender(GameState state, float scale)
    {
        var isSave = _mode == EditorDialogMode.Save;
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var panel = EditorDialogLayout.Panel(w, h, !isSave, scale);

        PlaceBox(_backdrop, EditorDialogLayout.Backdrop(w, h));
        PlaceBox(_panel, panel);
        PlaceLabel(_title, EditorDialogLayout.Title(panel, scale), isSave ? "Save Scene" : "Load Scene",
            EditorTheme.Text0, scale);

        // Cancel is always present (Load's only button; Save's second button).
        var cancel = EditorDialogLayout.CancelButton(panel, !isSave, scale);
        PlaceBox(_cancelBox, cancel);
        SetBoxFill(_cancelBox, DialogButtonFill(ref _cancelHover, 1, disabled: false, state.Time));
        PlaceLabel(_cancelLabel, LabelInset(cancel, scale), "Cancel", EditorTheme.Text0, scale);

        if (!_browser.Resolved)
        {
            // No project root: show the actionable message, hide the browser controls.
            PlaceLabel(_message, EditorDialogLayout.Message(panel, scale),
                _browser.Message ?? "No project root resolved.", EditorTheme.Text0, scale);
            Park(_breadcrumb); ParkBox(_upBox); Park(_upLabel);
            ParkBox(_fieldBox); Park(_fieldText);
            ParkBox(_confirmBox); Park(_confirmLabel);
            ParkRows();
            return;
        }

        // Breadcrumb (current path) + up-directory button.
        var breadcrumb = EditorDialogLayout.Breadcrumb(panel, scale);
        PlaceLabel(_breadcrumb, LabelInset(breadcrumb, scale), _browser.BreadcrumbText,
            EditorTheme.Text0, scale);
        var up = EditorDialogLayout.UpButton(panel, scale);
        PlaceBox(_upBox, up);
        SetBoxFill(_upBox, DialogButtonFill(ref _upHover, 2, disabled: !_browser.CanGoUp, state.Time));
        PlaceLabel(_upLabel, LabelInset(up, scale), "Up",
            _browser.CanGoUp ? EditorTheme.Text0 : EditorTheme.TextDisabled, scale);

        if (isSave)
        {
            var confirm = EditorDialogLayout.ConfirmButton(panel, scale);
            PlaceBox(_confirmBox, confirm);
            SetBoxFill(_confirmBox, DialogButtonFill(ref _confirmHover, 0, disabled: false, state.Time));
            PlaceLabel(_confirmLabel, LabelInset(confirm, scale), "Save", EditorTheme.Text0, scale);

            var field = EditorDialogLayout.Field(panel, scale);
            PlaceBox(_fieldBox, field);
            var caretOn = (state.TotalTime % 1.0) < 0.5;
            var shown = _field.Value + (caretOn ? "|" : string.Empty);
            PlaceLabel(_fieldText, EditorDialogLayout.FieldText(field, scale), shown,
                EditorTheme.Text0, scale);
        }
        else
        {
            ParkBox(_confirmBox); Park(_confirmLabel);
            ParkBox(_fieldBox); Park(_fieldText);
        }

        RenderList(panel, isSave, scale);
    }

    private void RenderList(Rectangle panel, bool isSave, float scale)
    {
        var count = _browser.EntryCount;
        if (count == 0)
        {
            PlaceLabel(_message, EditorDialogLayout.Message(panel, scale),
                _browser.Message ?? "Empty folder.", EditorTheme.Text0, scale);
            ParkRows();
            return;
        }

        Park(_message);
        var visible = EditorDialogLayout.VisibleRowCount(panel, isSave, scale);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, count - visible));

        for (var vi = 0; vi < visible; vi++)
        {
            var i = _scroll + vi;
            EnsureRow(vi);
            var (box, label) = _rows[vi];
            if (i >= count) { ParkBox(box); Park(label); continue; }

            var rect = EditorDialogLayout.Row(panel, vi, scale);
            PlaceBox(box, rect);
            SetBoxFill(box, _hoverControl == 3 + vi ? EditorTheme.Bg3 : EditorTheme.Bg2); // instant row hover
            var isDir = _browser.IsDirectory(i);
            // Folders are suffixed "/" (and tinted with the accent) so they read distinctly from files.
            var text = isDir ? _browser.Directories[i] + "/" : _browser.Files[i - _browser.Directories.Count];
            var color = isDir ? EditorTheme.Success : EditorTheme.Text0;
            PlaceLabel(label, LabelInset(rect, scale), text, color, scale);
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
        return EditorDialogLayout.TextInset(rect, labelH, scale);
    }

    // ─── entity construction (chrome: Editor target, no VisibleComponent) ────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        _backdrop = CreateBox(EditorTheme.Bg0, EditorTheme.Bg0, 0f, EditorTheme.Depths.DialogBackdrop);
        _panel = CreateBox(EditorTheme.Bg1, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogPanel);
        _title = CreateLabel(EditorTheme.Depths.DialogLabel);
        _breadcrumb = CreateLabel(EditorTheme.Depths.DialogLabel);
        _upBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _upLabel = CreateLabel(EditorTheme.Depths.DialogLabel);
        _fieldBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _fieldText = CreateLabel(EditorTheme.Depths.DialogLabel);
        _confirmBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _confirmLabel = CreateLabel(EditorTheme.Depths.DialogLabel);
        _cancelBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _cancelLabel = CreateLabel(EditorTheme.Depths.DialogLabel);
        _message = CreateLabel(EditorTheme.Depths.DialogLabel);
        _built = true;
        ParkAll();
    }

    private void EnsureRow(int index)
    {
        while (_rows.Count <= index)
        {
            var box = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1f, EditorTheme.Depths.DialogControl);
            var label = CreateLabel(EditorTheme.Depths.DialogLabel);
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
            Color = EditorTheme.Text0,
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
        Park(_breadcrumb); ParkBox(_upBox); Park(_upLabel);
        ParkBox(_fieldBox); Park(_fieldText);
        ParkBox(_confirmBox); Park(_confirmLabel);
        ParkBox(_cancelBox); Park(_cancelLabel);
        Park(_message);
        ParkRows();
    }

    private void ParkRows()
    {
        foreach (var (box, label) in _rows) { ParkBox(box); Park(label); }
    }

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
