#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
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
    /// <summary>The three-action Save dialog: Save Scene / Save Project / Save Backup As….</summary>
    Save,
    /// <summary>The confirm-on-switch modal (UX-C): "Unsaved changes in &lt;scene&gt;" with
    /// [Save &amp; Switch] [Discard &amp; Switch] [Cancel]. A plain 3-action confirm on the same modal
    /// machinery (parked chrome, cursor consume, same weave).</summary>
    ConfirmSwitch,

    /// <summary>The Create-Empty-Scene modal (UX2-D §4): a name field (prefilled <c>untitled</c>,
    /// <c>Sanitize</c>d) + [Create] [Cancel], opened from the Scenes-panel context menu. On confirm it
    /// refuses an existing name (loud, stays open) then writes a minimal canonical scene + switches to
    /// it. Same modal machinery (parked chrome, cursor consume, same weave).</summary>
    CreateScene,
}

/// <summary>
/// The editor's modal <b>Save dialog</b> — native-resolution chrome on <c>RenderTargetID.Editor</c>
/// (screen-space, DPR-scaled), built from the same engine primitives as the rest of the shell
/// (<see cref="SimpleButtonComponent"/> fills/outlines prepped by the woven <c>ButtonMeshPrepSystem</c>,
/// <see cref="DynamicTextComponent"/> labels), laid out by the pure <see cref="EditorDialogLayout"/> and
/// hit-tested against the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/> — never
/// <c>VirtualPosition</c>, which is frozen over the chrome margins. Per the chrome rule the dialog
/// entities carry <b>no</b> <c>VisibleComponent</c>; they are shown/hidden by parking off-screen (the
/// SystemsPanel idiom).
///
/// <para><b>The three actions (UX-D §4).</b> The Save dialog replaces the removed file-system navigator
/// with three stacked full-width actions, each a title + a subtitle:</para>
/// <list type="number">
///   <item><b>Save Scene</b> (primary, <see cref="EditorTheme.Accent"/>) — the guarded save of the
///   current scene id (source-tree write + zero-touch bundling + save-point mark). Enter picks it.</item>
///   <item><b>Save Project</b> — v1 saves the same single in-memory scene through the same path; it is
///   the terrain for multi-scene sessions and never blanket-writes scenes not in memory.</item>
///   <item><b>Save Backup As…</b> — clicking it <b>arms</b> a name field (prefilled
///   <c>&lt;sceneId&gt;-backup</c>) + a Confirm; confirming writes <c>&lt;name&gt;.mdscene</c> WITHOUT
///   rebinding the scene id / marking the save point / bundling, then reloads the bound scene from disk
///   (via the transport's Restart) — the working scene returns to its on-disk truth.</item>
/// </list>
/// The dialog stays <b>game-agnostic</b>: each action fires a callback the overlay supplies (which run
/// the SAME guarded Save / Restart paths the toolbar uses), so this system knows nothing of
/// <c>SceneWriter</c> / project paths.
///
/// <para><b>Modality (the dialog owns input while open).</b> Two facets: (1) <b>mouse</b> — after
/// hit-testing its own controls, the system <b>consumes the cursor's pointer edges</b> (clears the
/// press/release/scroll edges on the single cursor entity), so every mouse-driven editor system
/// downstream this frame sees no click and does not act — no per-system edit needed. (2)
/// <b>keyboard</b> — the composing screen wires the host keyboard system's <c>ShouldSuppressInput</c>
/// to <see cref="IsOpen"/>, so every editor/game key (delete, undo/redo, the game's Escape-to-exit)
/// stands down while the dialog reads the keyboard for the backup name field. Escape cancels; Enter
/// confirms (Save Scene, or the backup when its field is armed). The release-edge action survives its
/// own consume only because the cursor derives its edges from its OWN previous-state (the EF1 cursor
/// premise), not from the level fields the dialog clears.</para>
///
/// <para><b>Headless-drivable.</b> Every action has a public method (<see cref="OpenSave"/> /
/// <see cref="SaveScene"/> / <see cref="SaveProject"/> / <see cref="ArmBackup"/> / <see cref="SetName"/> /
/// <see cref="ConfirmBackup"/> / <see cref="Backup"/> / <see cref="Confirm"/> / <see cref="Cancel"/>) so
/// the <c>dialog:*</c> editor-op grammar (<c>save-open</c> / <c>scene</c> / <c>project</c> / <c>name</c>
/// / <c>backup</c> / <c>confirm</c> / <c>discard</c> / <c>cancel</c>) drives the full flow with no real
/// keyboard/mouse — see <c>EditorOverlay.DispatchNamedAction</c>.</para>
/// </summary>
public sealed class EditorDialogSystem : ISystem<GameState>
{
    // Colors + depths come from EditorTheme (the module's single source): the dialog band sits ABOVE
    // the shell chrome (panels 0.1 / buttons 0.5 / labels 0.6) so the modal covers the toolbar + panel.

    // Per-widget hover-fade progress (persistent buttons store it on the system, never a pooled row —
    // pre-mortem #6). Advanced framerate-independently.
    private readonly float[] _actionHover = new float[EditorDialogLayout.SaveActionCount];
    private float _confirmHover, _cancelHover, _discardHover;
    // Which control the cursor is over: 0..2 = a Save action row, 3 = the backup/Save&Switch confirm,
    // 4 = Cancel, 5 = the confirm-switch Discard, or -1 = none.
    private int _hoverControl = -1;
    private bool _leftDown;

    // Confirm-on-switch state (UX-C): the callbacks for the current confirm, set by OpenConfirmSwitch.
    private Action<GameState>? _onSwitchConfirmed; // Save & Switch (the primary/Enter action)
    private Action<GameState>? _onSwitchDiscarded; // Discard & Switch
    private string _confirmMessage = string.Empty;

    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Action<GameState> _onSaveScene;
    private readonly Action<GameState> _onSaveProject;
    private readonly Action<string, GameState> _onSaveBackup;
    // Create Empty Scene (UX2-D): a name-collision predicate (loud refuse + keep open) and the create
    // callback (write + bundle + switch); both null on a composition that offers no scene creation.
    private readonly Func<string, bool>? _onSceneNameExists;
    private readonly Action<string, GameState>? _onCreateScene;
    private readonly Func<KeyboardState> _getKeyboardState;
    private readonly EntitySet _cursorSet;

    private readonly EditorTextField _field = new();
    private EditorDialogMode _mode = EditorDialogMode.None;
    private string _sceneId = string.Empty;
    private bool _backupActive; // the Save Backup As… name field is revealed/armed
    private KeyboardState _prevKeys;

    private bool _built;
    private Entity _backdrop, _panel, _title, _fieldBox, _fieldText,
        _confirmBox, _confirmLabel, _discardBox, _discardLabel, _cancelBox, _cancelLabel, _message;
    private readonly Entity[] _actionBox = new Entity[EditorDialogLayout.SaveActionCount];
    private readonly Entity[] _actionTitle = new Entity[EditorDialogLayout.SaveActionCount];
    private readonly Entity[] _actionSub = new Entity[EditorDialogLayout.SaveActionCount];

    public bool IsEnabled { get; set; } = true;

    /// <summary>True while any dialog is open — the screen wires this to the host keyboard system's
    /// <c>ShouldSuppressInput</c> so editor/game keys stand down while the dialog owns input.</summary>
    public bool IsOpen => _mode != EditorDialogMode.None;

    /// <summary>The open dialog kind (or <see cref="EditorDialogMode.None"/>). Exposed for tests.</summary>
    public EditorDialogMode Mode => _mode;

    /// <summary>Whether the Save Backup As… name field is currently armed/revealed. Exposed for tests.</summary>
    public bool IsBackupArmed => _backupActive;

    /// <summary>The current backup-name field value (raw, pre-sanitize). Exposed for tests.</summary>
    public string NameValue => _field.Value;

    public EditorDialogSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        Action<GameState> onSaveScene,
        Action<GameState> onSaveProject,
        Action<string, GameState> onSaveBackup,
        Func<KeyboardState>? getKeyboardState = null,
        Func<string, bool>? onSceneNameExists = null,
        Action<string, GameState>? onCreateScene = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _onSaveScene = onSaveScene ?? throw new ArgumentNullException(nameof(onSaveScene));
        _onSaveProject = onSaveProject ?? throw new ArgumentNullException(nameof(onSaveProject));
        _onSaveBackup = onSaveBackup ?? throw new ArgumentNullException(nameof(onSaveBackup));
        _onSceneNameExists = onSceneNameExists;
        _onCreateScene = onCreateScene;
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    // ─── public API (toolbar dispatch, headless ops, tests) ──────────────────────────────────────

    /// <summary>Opens the Save dialog for scene id <paramref name="sceneId"/> (used in the subtitles and
    /// as the <c>&lt;sceneId&gt;-backup</c> field prefill). The backup field starts disarmed.</summary>
    public void OpenSave(string sceneId)
    {
        EnsureBuilt();
        _sceneId = sceneId ?? string.Empty;
        _backupActive = false;
        _field.Set(EditorTextField.Sanitize(_sceneId + "-backup"));
        _prevKeys = _getKeyboardState(); // swallow the current key state so no stale edge fires
        _mode = EditorDialogMode.Save;
    }

    /// <summary>Opens the confirm-on-switch modal (UX-C): the message names
    /// <paramref name="sceneId"/> (the scene with unsaved edits), and the three actions route to
    /// <paramref name="onSaveAndSwitch"/> (Save &amp; Switch / Enter), <paramref name="onDiscardAndSwitch"/>
    /// (Discard &amp; Switch), and Cancel (Escape / close, invokes neither). The caller (the scene-select
    /// gate) supplies the callbacks so the dialog stays game-agnostic.</summary>
    public void OpenConfirmSwitch(string sceneId, Action<GameState> onSaveAndSwitch, Action<GameState> onDiscardAndSwitch)
    {
        EnsureBuilt();
        _onSwitchConfirmed = onSaveAndSwitch ?? throw new ArgumentNullException(nameof(onSaveAndSwitch));
        _onSwitchDiscarded = onDiscardAndSwitch ?? throw new ArgumentNullException(nameof(onDiscardAndSwitch));
        _confirmMessage = $"Unsaved changes in {sceneId}.";
        _prevKeys = _getKeyboardState();
        _mode = EditorDialogMode.ConfirmSwitch;
    }

    /// <summary>Opens the Create-Empty-Scene modal (UX2-D §4): a name field prefilled <c>untitled</c>
    /// (<c>Sanitize</c>d) + [Create] [Cancel]. Opened from the Scenes-panel context menu
    /// (<c>menu:pick create-scene</c>); confirm goes through <see cref="ConfirmCreateScene"/>.</summary>
    public void OpenCreateScene()
    {
        EnsureBuilt();
        _field.Set(EditorTextField.Sanitize("untitled"));
        _prevKeys = _getKeyboardState(); // swallow the current key state so no stale edge fires
        _mode = EditorDialogMode.CreateScene;
    }

    /// <summary>Confirms Create Empty Scene (headless <c>dialog:confirm</c> / Enter / the Create button):
    /// sanitizes the field, <b>refuses loudly and stays open</b> on an empty result OR an existing name
    /// (via the injected collision predicate), else closes and runs the create callback (write the
    /// minimal scene + switch). No-op outside <see cref="EditorDialogMode.CreateScene"/>.</summary>
    public void ConfirmCreateScene(GameState state)
    {
        if (_mode != EditorDialogMode.CreateScene) return;
        var id = EditorTextField.Sanitize(_field.Value);
        if (string.IsNullOrEmpty(id))
        {
            Logger.Warning(
                "[level-editor] Create Empty Scene: the name is empty after reducing it to a safe file id " +
                "(letters, digits, '-' and '_'). Type a valid name.");
            return; // keep the dialog open
        }
        if (_onSceneNameExists?.Invoke(id) == true)
        {
            Logger.Warning(
                $"[level-editor] Create Empty Scene refused: a scene named '{id}' already exists. " +
                "Choose a different name.");
            return; // keep the dialog open (loud refusal)
        }
        var action = _onCreateScene;
        Close();
        action?.Invoke(id, state);
    }

    /// <summary>The <b>Save Scene</b> action (headless <c>dialog:scene</c>): closes, then runs the
    /// guarded save-current-scene callback. No-op outside <see cref="EditorDialogMode.Save"/>.</summary>
    public void SaveScene(GameState state)
    {
        if (_mode != EditorDialogMode.Save) return;
        var action = _onSaveScene;
        Close();
        action.Invoke(state);
    }

    /// <summary>The <b>Save Project</b> action (headless <c>dialog:project</c>): closes, then runs the
    /// save-project callback (v1 = the same single scene through the same guarded path). No-op outside
    /// <see cref="EditorDialogMode.Save"/>.</summary>
    public void SaveProject(GameState state)
    {
        if (_mode != EditorDialogMode.Save) return;
        var action = _onSaveProject;
        Close();
        action.Invoke(state);
    }

    /// <summary>Arms the <b>Save Backup As…</b> action: reveals the name field (prefilled at
    /// <see cref="OpenSave"/>). No-op outside <see cref="EditorDialogMode.Save"/>. A click on the backup
    /// row calls this; a subsequent confirm writes the backup.</summary>
    public void ArmBackup()
    {
        if (_mode != EditorDialogMode.Save) return;
        _backupActive = true;
    }

    /// <summary>Replaces the backup-name field value (the headless <c>dialog:name</c> op).</summary>
    public void SetName(string text) => _field.Set(text);

    /// <summary>Confirms the backup: sanitizes the field to a safe file id and, when non-empty, closes
    /// and runs the backup callback (write <c>&lt;name&gt;.mdscene</c> then Restart). An empty result
    /// keeps the dialog open and logs. No-op unless the backup field is armed.</summary>
    public void ConfirmBackup(GameState state)
    {
        if (_mode != EditorDialogMode.Save || !_backupActive) return;
        var id = EditorTextField.Sanitize(_field.Value);
        if (string.IsNullOrEmpty(id))
        {
            Logger.Warning(
                "[level-editor] Save Backup As: the name is empty after reducing it to a safe file id " +
                "(letters, digits, '-' and '_'). Type a valid name.");
            return; // keep the dialog open
        }
        var action = _onSaveBackup;
        Close();
        action.Invoke(id, state);
    }

    /// <summary>One-shot backup (the headless <c>dialog:backup &lt;name&gt;</c> op): arm the field, set
    /// the name, and confirm in a single call. No-op outside <see cref="EditorDialogMode.Save"/>.</summary>
    public void Backup(string name, GameState state)
    {
        if (_mode != EditorDialogMode.Save) return;
        _backupActive = true;
        _field.Set(name);
        ConfirmBackup(state);
    }

    /// <summary>The confirm-switch dialog's <b>Discard &amp; Switch</b> action (the headless
    /// <c>dialog:discard</c> op / the Danger button): closes, then invokes the discard callback
    /// (switch without saving). No-op outside <see cref="EditorDialogMode.ConfirmSwitch"/>.</summary>
    public void Discard(GameState state)
    {
        if (_mode != EditorDialogMode.ConfirmSwitch) return;
        var action = _onSwitchDiscarded;
        Close();
        action?.Invoke(state);
    }

    /// <summary>The focused/default confirm (the headless <c>dialog:confirm</c> op / Enter): in
    /// <see cref="EditorDialogMode.ConfirmSwitch"/> it is Save &amp; Switch; in the Save dialog it is
    /// <see cref="ConfirmBackup"/> while the backup field is armed, else <see cref="SaveScene"/>.</summary>
    public void Confirm(GameState state)
    {
        switch (_mode)
        {
            case EditorDialogMode.ConfirmSwitch:
                var action = _onSwitchConfirmed;
                Close();
                action?.Invoke(state);
                return;
            case EditorDialogMode.Save:
                if (_backupActive) ConfirmBackup(state);
                else SaveScene(state);
                return;
            case EditorDialogMode.CreateScene:
                ConfirmCreateScene(state);
                return;
        }
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

        if (_mode == EditorDialogMode.ConfirmSwitch)
        {
            ReadConfirmKeyboard(state);
            HandleConfirmMouseAndConsume(state, scale);
            if (_mode == EditorDialogMode.None) { ParkAll(); return; }
            LayoutConfirm(state, scale);
            return;
        }

        if (_mode == EditorDialogMode.CreateScene)
        {
            ReadCreateSceneKeyboard(state);
            HandleCreateSceneMouseAndConsume(state, scale);
            if (_mode == EditorDialogMode.None) { ParkAll(); return; }
            LayoutCreateScene(state, scale);
            return;
        }

        // Save mode.
        ReadSaveKeyboard(state);
        HandleSaveMouseAndConsume(state, scale);
        if (_mode == EditorDialogMode.None) { ParkAll(); return; }
        LayoutSave(state, scale);
    }

    /// <summary>Confirm-switch keyboard: Enter = Save &amp; Switch (the primary), Escape = Cancel.</summary>
    private void ReadConfirmKeyboard(GameState state)
    {
        var keys = _getKeyboardState();
        var enter = keys.IsKeyDown(Keys.Enter) && !_prevKeys.IsKeyDown(Keys.Enter);
        var escape = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        _prevKeys = keys;
        if (enter) Confirm(state);
        else if (escape) Cancel();
    }

    /// <summary>Save-dialog keyboard: Escape cancels; Enter confirms (Save Scene, or the backup when its
    /// field is armed). While the backup field is armed, typed characters edit the name (Backspace too);
    /// otherwise typing is ignored (the actions are click/Enter-driven).</summary>
    private void ReadSaveKeyboard(GameState state)
    {
        var keys = _getKeyboardState();
        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key)) continue; // only newly-pressed this frame
            switch (key)
            {
                case Keys.Enter: _prevKeys = keys; Confirm(state); return;
                case Keys.Escape: _prevKeys = keys; Cancel(); return;
                case Keys.Back: if (_backupActive) _field.Backspace(); continue;
            }
            if (!_backupActive) continue;
            var c = KeyToChar(key);
            if (c != '\0') _field.Append(c);
        }
        _prevKeys = keys;
    }

    // ─── mouse (Save dialog) ───────────────────────────────────────────────────────────────────

    /// <summary>Hit-tests the Save dialog's controls against the cursor's native <c>ScreenPosition</c>,
    /// then <b>consumes</b> the cursor's pointer edges so no editor system downstream acts on the same
    /// click this frame (the mouse half of the modal capture).</summary>
    private void HandleSaveMouseAndConsume(GameState state, float scale)
    {
        _hoverControl = -1;
        _leftDown = false;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            var panel = EditorDialogLayout.SavePanel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight,
                _backupActive, scale);

            _leftDown = input.LeftButton;
            _hoverControl = ComputeSaveHover(panel, scale, point);

            if (input.LeftButtonReleased)
            {
                if (EditorDialogLayout.SaveAction(panel, 0, scale).Contains(point)) SaveScene(state);
                else if (EditorDialogLayout.SaveAction(panel, 1, scale).Contains(point)) SaveProject(state);
                else if (EditorDialogLayout.SaveAction(panel, 2, scale).Contains(point)) ArmBackup();
                else if (_backupActive && EditorDialogLayout.BackupConfirmButton(panel, scale).Contains(point)) ConfirmBackup(state);
                else if (EditorDialogLayout.SaveCancelButton(panel, scale).Contains(point)) Cancel();
            }

            ConsumeCursor(ref input);
            cursor.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    /// <summary>Which Save control the cursor is over (mirrors the release hit-test order so hover and
    /// click agree): 0..2 = an action row, 3 = the backup Confirm, 4 = Cancel, or -1.</summary>
    private int ComputeSaveHover(Rectangle panel, float scale, Point point)
    {
        for (var i = 0; i < EditorDialogLayout.SaveActionCount; i++)
            if (EditorDialogLayout.SaveAction(panel, i, scale).Contains(point)) return i;
        if (_backupActive && EditorDialogLayout.BackupConfirmButton(panel, scale).Contains(point)) return 3;
        if (EditorDialogLayout.SaveCancelButton(panel, scale).Contains(point)) return 4;
        return -1;
    }

    // ─── mouse (confirm-switch) ─────────────────────────────────────────────────────────────────

    /// <summary>Hit-tests the confirm-switch buttons against the cursor's native <c>ScreenPosition</c>
    /// and consumes the cursor edges (modal), exactly like <see cref="HandleSaveMouseAndConsume"/>.</summary>
    private void HandleConfirmMouseAndConsume(GameState state, float scale)
    {
        _hoverControl = -1;
        _leftDown = false;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            var panel = EditorDialogLayout.ConfirmPanel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);
            var buttons = EditorDialogLayout.ConfirmButtons(panel, scale);

            _leftDown = input.LeftButton;
            if (buttons[0].Contains(point)) _hoverControl = 3;       // Save & Switch
            else if (buttons[1].Contains(point)) _hoverControl = 5;  // Discard & Switch
            else if (buttons[2].Contains(point)) _hoverControl = 4;  // Cancel

            if (input.LeftButtonReleased)
            {
                if (buttons[0].Contains(point)) Confirm(state);
                else if (buttons[1].Contains(point)) Discard(state);
                else if (buttons[2].Contains(point)) Cancel();
            }

            ConsumeCursor(ref input);
            cursor.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    /// <summary>Clears the cursor's pointer edges + button level fields for this frame (the modal
    /// consume). The dialog's own release-edge action survives because <c>CursorInputSystem</c> derives
    /// its edges from its own previous hardware state, not these fields (the EF1 cursor premise).</summary>
    private static void ConsumeCursor(ref CursorInputComponent input)
    {
        input.LeftButtonPressed = input.RightButtonPressed = input.MiddleButtonPressed = false;
        input.LeftButtonReleased = input.RightButtonReleased = input.MiddleButtonReleased = false;
        input.LeftButton = input.RightButton = input.MiddleButton = false;
        input.ScrollWheelDelta = 0;
    }

    // ─── layout + render (Save dialog) ───────────────────────────────────────────────────────────

    private void LayoutSave(GameState state, float scale)
    {
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var panel = EditorDialogLayout.SavePanel(w, h, _backupActive, scale);

        PlaceBox(_backdrop, EditorDialogLayout.Backdrop(w, h));
        PlaceBox(_panel, panel);
        PlaceLabel(_title, EditorDialogLayout.Title(panel, scale), "Save", EditorTheme.Text0, scale);

        // The three actions (Save Scene is the primary — Accent outline).
        PlaceAction(0, panel, scale, state, primary: true,
            "Save Scene", $"{_sceneId}{SceneWriter.SceneFileExtension}", EditorTheme.Text1);
        PlaceAction(1, panel, scale, state, primary: false,
            "Save Project", $"every unsaved scene + project files (currently: {_sceneId})", EditorTheme.Text1);
        PlaceAction(2, panel, scale, state, primary: false,
            "Save Backup As...", $"then reloads {_sceneId} from disk (discards unsaved edits)", EditorTheme.Warning);

        // The backup name field + Confirm (revealed only when armed).
        if (_backupActive)
        {
            var field = EditorDialogLayout.BackupField(panel, scale);
            PlaceBox(_fieldBox, field);
            SetBoxFill(_fieldBox, EditorTheme.Bg2);
            var caretOn = (state.TotalTime % 1.0) < 0.5;
            var shown = _field.Value + (caretOn ? "|" : string.Empty);
            PlaceLabel(_fieldText, EditorDialogLayout.FieldText(field, scale), shown, EditorTheme.Text0, scale);

            var confirm = EditorDialogLayout.BackupConfirmButton(panel, scale);
            PlaceBox(_confirmBox, confirm);
            SetBoxFill(_confirmBox, DialogButtonFill(ref _confirmHover, 3, disabled: false, state.Time));
            PlaceLabel(_confirmLabel, LabelInset(confirm, scale), "Confirm", EditorTheme.Text0, scale);
        }
        else
        {
            ParkBox(_fieldBox); Park(_fieldText);
            ParkBox(_confirmBox); Park(_confirmLabel);
        }

        // Cancel is always present.
        var cancel = EditorDialogLayout.SaveCancelButton(panel, scale);
        PlaceBox(_cancelBox, cancel);
        SetBoxFill(_cancelBox, DialogButtonFill(ref _cancelHover, 4, disabled: false, state.Time));
        PlaceLabel(_cancelLabel, LabelInset(cancel, scale), "Cancel", EditorTheme.Text0, scale);

        // Park confirm-switch-only chrome.
        Park(_message);
        ParkBox(_discardBox); Park(_discardLabel);
    }

    /// <summary>Places one Save action row (box + title line + subtitle line). The primary action gets an
    /// <see cref="EditorTheme.Accent"/> outline; the fill eases through the shared hover recipe.</summary>
    private void PlaceAction(int index, Rectangle panel, float scale, GameState state, bool primary,
        string title, string subtitle, Color subtitleColor)
    {
        var rect = EditorDialogLayout.SaveAction(panel, index, scale);
        PlaceBox(_actionBox[index], rect);
        SetBoxFill(_actionBox[index], DialogButtonFill(ref _actionHover[index], index, disabled: false, state.Time));
        SetBoxOutline(_actionBox[index], primary ? EditorTheme.Accent : EditorTheme.BorderStrong);
        PlaceLabel(_actionTitle[index], EditorDialogLayout.ActionTitle(rect, scale), title,
            primary ? EditorTheme.Accent : EditorTheme.Text0, scale);
        PlaceLabel(_actionSub[index], EditorDialogLayout.ActionSubtitle(rect, scale), subtitle, subtitleColor, scale);
    }

    // ─── layout + render (confirm-switch) ───────────────────────────────────────────────────────

    /// <summary>Lays out the confirm-switch modal: backdrop + panel + title + message + the three
    /// buttons (Discard styled <see cref="EditorTheme.Danger"/>); parks every Save-only control.</summary>
    private void LayoutConfirm(GameState state, float scale)
    {
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var panel = EditorDialogLayout.ConfirmPanel(w, h, scale);
        var buttons = EditorDialogLayout.ConfirmButtons(panel, scale);

        PlaceBox(_backdrop, EditorDialogLayout.Backdrop(w, h));
        PlaceBox(_panel, panel);
        PlaceLabel(_title, EditorDialogLayout.Title(panel, scale), "Unsaved changes", EditorTheme.Text0, scale);
        PlaceLabel(_message, EditorDialogLayout.ConfirmMessage(panel, scale), _confirmMessage, EditorTheme.Text1, scale);

        PlaceBox(_confirmBox, buttons[0]);
        SetBoxOutline(_confirmBox, EditorTheme.BorderStrong);
        SetBoxFill(_confirmBox, DialogButtonFill(ref _confirmHover, 3, disabled: false, state.Time));
        PlaceLabel(_confirmLabel, LabelInset(buttons[0], scale), "Save & Switch", EditorTheme.Text0, scale);

        PlaceBox(_discardBox, buttons[1]);
        SetBoxFill(_discardBox, DialogButtonFill(ref _discardHover, 5, disabled: false, state.Time));
        PlaceLabel(_discardLabel, LabelInset(buttons[1], scale), "Discard & Switch", EditorTheme.Danger, scale);

        PlaceBox(_cancelBox, buttons[2]);
        SetBoxFill(_cancelBox, DialogButtonFill(ref _cancelHover, 4, disabled: false, state.Time));
        PlaceLabel(_cancelLabel, LabelInset(buttons[2], scale), "Cancel", EditorTheme.Text0, scale);

        // Park the Save-only chrome (action rows + backup field).
        ParkBox(_fieldBox); Park(_fieldText);
        for (var i = 0; i < EditorDialogLayout.SaveActionCount; i++)
        {
            ParkBox(_actionBox[i]); Park(_actionTitle[i]); Park(_actionSub[i]);
        }
    }

    // ─── Create Empty Scene (UX2-D §4) ────────────────────────────────────────────────────────────

    /// <summary>Create-scene keyboard: Enter = Create, Escape = Cancel, Backspace edits, and (unlike the
    /// Save dialog's conditional field) typed characters ALWAYS edit the name — the field is the modal's
    /// whole point.</summary>
    private void ReadCreateSceneKeyboard(GameState state)
    {
        var keys = _getKeyboardState();
        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key)) continue; // only newly-pressed this frame
            switch (key)
            {
                case Keys.Enter: _prevKeys = keys; Confirm(state); return;
                case Keys.Escape: _prevKeys = keys; Cancel(); return;
                case Keys.Back: _field.Backspace(); continue;
            }
            var c = KeyToChar(key);
            if (c != '\0') _field.Append(c);
        }
        _prevKeys = keys;
    }

    /// <summary>Hit-tests the Create-scene modal's Create/Cancel buttons against the cursor's native
    /// <c>ScreenPosition</c> (reusing the Save dialog's bottom-right button geometry), then consumes the
    /// cursor edges (the mouse half of the modal capture).</summary>
    private void HandleCreateSceneMouseAndConsume(GameState state, float scale)
    {
        _hoverControl = -1;
        _leftDown = false;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            var panel = EditorDialogLayout.CreateScenePanel(
                _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);

            _leftDown = input.LeftButton;
            if (EditorDialogLayout.BackupConfirmButton(panel, scale).Contains(point)) _hoverControl = 3;      // Create
            else if (EditorDialogLayout.SaveCancelButton(panel, scale).Contains(point)) _hoverControl = 4;    // Cancel

            if (input.LeftButtonReleased)
            {
                if (EditorDialogLayout.BackupConfirmButton(panel, scale).Contains(point)) ConfirmCreateScene(state);
                else if (EditorDialogLayout.SaveCancelButton(panel, scale).Contains(point)) Cancel();
            }

            ConsumeCursor(ref input);
            cursor.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    /// <summary>Lays out the Create-scene modal: backdrop + panel + title + the name field (with a
    /// blinking caret) + [Create] (Accent label) [Cancel]; parks every Save-/confirm-only control.</summary>
    private void LayoutCreateScene(GameState state, float scale)
    {
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var panel = EditorDialogLayout.CreateScenePanel(w, h, scale);

        PlaceBox(_backdrop, EditorDialogLayout.Backdrop(w, h));
        PlaceBox(_panel, panel);
        PlaceLabel(_title, EditorDialogLayout.Title(panel, scale), "New Scene", EditorTheme.Text0, scale);

        var field = EditorDialogLayout.CreateSceneField(panel, scale);
        PlaceBox(_fieldBox, field);
        SetBoxFill(_fieldBox, EditorTheme.Bg2);
        var caretOn = (state.TotalTime % 1.0) < 0.5;
        var shown = _field.Value + (caretOn ? "|" : string.Empty);
        PlaceLabel(_fieldText, EditorDialogLayout.FieldText(field, scale), shown, EditorTheme.Text0, scale);

        var create = EditorDialogLayout.BackupConfirmButton(panel, scale);
        PlaceBox(_confirmBox, create);
        SetBoxFill(_confirmBox, DialogButtonFill(ref _confirmHover, 3, disabled: false, state.Time));
        PlaceLabel(_confirmLabel, LabelInset(create, scale), "Create", EditorTheme.Accent, scale);

        var cancel = EditorDialogLayout.SaveCancelButton(panel, scale);
        PlaceBox(_cancelBox, cancel);
        SetBoxFill(_cancelBox, DialogButtonFill(ref _cancelHover, 4, disabled: false, state.Time));
        PlaceLabel(_cancelLabel, LabelInset(cancel, scale), "Cancel", EditorTheme.Text0, scale);

        // Park the Save- + confirm-switch-only chrome (action rows, message, discard).
        Park(_message);
        ParkBox(_discardBox); Park(_discardLabel);
        for (var i = 0; i < EditorDialogLayout.SaveActionCount; i++)
        {
            ParkBox(_actionBox[i]); Park(_actionTitle[i]); Park(_actionSub[i]);
        }
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

    private static void SetBoxOutline(Entity e, Color outline)
    {
        if (!e.IsAlive) return;
        e.Get<SimpleButtonComponent>().Color = outline;
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
        for (var i = 0; i < EditorDialogLayout.SaveActionCount; i++)
        {
            _actionBox[i] = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
            _actionTitle[i] = CreateLabel(EditorTheme.Depths.DialogLabel);
            _actionSub[i] = CreateLabel(EditorTheme.Depths.DialogLabel);
        }
        _fieldBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _fieldText = CreateLabel(EditorTheme.Depths.DialogLabel);
        _confirmBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _confirmLabel = CreateLabel(EditorTheme.Depths.DialogLabel);
        // The confirm-switch Discard button gets a Danger outline (its label is Danger-tinted too).
        _discardBox = CreateBox(EditorTheme.Bg2, EditorTheme.Danger, 1.5f, EditorTheme.Depths.DialogControl);
        _discardLabel = CreateLabel(EditorTheme.Depths.DialogLabel);
        _cancelBox = CreateBox(EditorTheme.Bg2, EditorTheme.BorderStrong, 1.5f, EditorTheme.Depths.DialogControl);
        _cancelLabel = CreateLabel(EditorTheme.Depths.DialogLabel);
        _message = CreateLabel(EditorTheme.Depths.DialogLabel);
        _built = true;
        ParkAll();
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
        ParkBox(_fieldBox); Park(_fieldText);
        ParkBox(_confirmBox); Park(_confirmLabel);
        ParkBox(_discardBox); Park(_discardLabel);
        ParkBox(_cancelBox); Park(_cancelLabel);
        Park(_message);
        for (var i = 0; i < EditorDialogLayout.SaveActionCount; i++)
        {
            ParkBox(_actionBox[i]); Park(_actionTitle[i]); Park(_actionSub[i]);
        }
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
