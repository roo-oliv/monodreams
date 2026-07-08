#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor toolbar's interaction system. It hit-tests the cursor against every
/// <see cref="ToolbarButtonComponent"/> and, on a click (left button released over a button), hands
/// the button's <see cref="EditorToolbarAction"/> plus the frame's <see cref="GameState"/> to a
/// dispatch callback the composer supplies — which wires the transport (Play/Pause / Restart
/// through <c>EditorTransport</c>), Save → <c>SceneWriter</c>, Load → publish
/// <c>LoadSceneRequest</c>, Undo/Redo → <c>EditorHistory</c>, and the tool/snap actions → the
/// shared <see cref="GizmoStateComponent"/>. It also tracks per-button hover, tints the button
/// fill, and keeps the Play/Pause toggle button's label in sync with the transport state.
///
/// <para><b>Transport model: live in BOTH modes.</b> Under the editor run configuration the shell
/// never collapses, so the toolbar hit-tests in both transport states. What changes with the state
/// is which buttons are active: the TRANSPORT buttons (<see cref="EditorToolbarAction.PlayPause"/>
/// / <see cref="EditorToolbarAction.Restart"/>) dispatch always — they are how you leave either
/// state — while the EDITING buttons (tools / Save / Load / Undo / Redo / Snap) dispatch only
/// while Paused (<see cref="RunMode.Edit"/>) and render with the disabled fill while Playing
/// (in Play a click belongs to the game; an undo racing live physics would be surprising).</para>
///
/// <para><b>Native screen-space hit-test (Wave 7).</b> The toolbar lives on the Editor render
/// target — native window resolution, composited 1:1 — so button <c>Bounds</c> are physical screen
/// pixels and the system tests the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/>
/// (hardware pixels, set before any letterbox/camera mapping) against them. The chrome sits in the
/// viewport-inset margins where the virtual mapping is null, so <c>VirtualPosition</c> (the old
/// HUD hit-test coordinate) would be frozen/stale there; <c>ScreenPosition</c> is always live.</para>
///
/// <para><b>Game-agnostic.</b> The system fires the action enum through the supplied callback so
/// <c>level-editor</c> needs no game type; the overlay owns the concrete transport / writer /
/// history and supplies the dispatch.</para>
/// </summary>
[With(typeof(ToolbarButtonComponent), typeof(TransformComponent))]
public sealed class ToolbarSystem : AEntitySetSystem<GameState>
{
    private readonly EntitySet _cursorSet;
    private readonly Action<EditorToolbarAction, GameState> _dispatch;
    private readonly Func<EditorToolbarAction, GameState, bool>? _isEditingActionBlocked;
    private readonly Func<bool>? _isInputSuppressed;

    private bool _cursorPresent;
    private Vector2 _cursorPoint;
    private bool _clicked;
    private bool _leftDown;

    /// <param name="world">The screen's world (the toolbar buttons + cursor live here).</param>
    /// <param name="dispatch">Fires a clicked button's action + the frame's state.</param>
    /// <param name="isEditingActionBlocked">Optional extra gate for an EDITING button beyond the
    /// transport rule: when it returns <c>true</c> the button renders with the disabled fill and its
    /// click is suppressed even while Paused. The overlay wires it to the save-guard's "no project
    /// root" cause so Save dims while the project is unresolved (the "Playing" cause is already
    /// covered by the transport rule). Null (the default) preserves the pre-PS2 behaviour.</param>
    /// <param name="isInputSuppressed">Optional global suppress: while it returns <c>true</c> the
    /// toolbar dispatches nothing (a shell splitter/scrollbar drag owns the pointer — a drag that
    /// happens to release over a toolbar button must not also fire it). Null (the default) never
    /// suppresses.</param>
    public ToolbarSystem(World world, Action<EditorToolbarAction, GameState> dispatch,
        Func<EditorToolbarAction, GameState, bool>? isEditingActionBlocked = null,
        Func<bool>? isInputSuppressed = null)
        : base(world.GetEntities().With<ToolbarButtonComponent>().With<TransformComponent>().AsSet())
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isEditingActionBlocked = isEditingActionBlocked;
        _isInputSuppressed = isInputSuppressed;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    protected override void PreUpdate(GameState state)
    {
        _cursorPresent = false;
        _clicked = false;
        _leftDown = false;

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            _cursorPresent = true;
            _cursorPoint = input.ScreenPosition; // native-pixel hit-test (Editor target chrome)
            _clicked = input.LeftButtonReleased;  // a click = press then release over the button
            _leftDown = input.LeftButton;         // held → the instant "pressed" fill
            return;
        }
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var button = ref entity.Get<ToolbarButtonComponent>();

        // The Play/Pause toggle button's label mirrors the transport state every frame.
        if (button.Action == EditorToolbarAction.PlayPause)
            SyncPlayPauseLabel(entity, state);

        // Transport buttons are live in both modes; editing buttons only while Paused (Edit) — and
        // an editing button may be additionally gated (e.g. Save while the project is unresolved).
        var active = (state.RunMode == RunMode.Edit || button.Action.IsTransport())
                     && !(_isEditingActionBlocked?.Invoke(button.Action, state) ?? false);

        var over = _cursorPresent && active && button.Bounds.Contains(_cursorPoint);
        button.IsHovered = over;

        // Per-widget hover fade (framerate-independent, ~120ms): idle Bg2 → hover Bg3. Stored on the
        // component so it survives frame to frame; a disabled button eases back to idle (over is false
        // while inactive). Pressed (held over the control) snaps to Bg4 instantly.
        button.HoverProgress = EditorTheme.AdvanceHover(button.HoverProgress, over, state.Time);

        if (entity.Has<SimpleButtonComponent>())
        {
            ref var visual = ref entity.Get<SimpleButtonComponent>();
            visual.FillColor = EditorTheme.ControlFill(
                disabled: !active, selected: false, pressed: over && _leftDown, button.HoverProgress);
            // Disabled buttons dim their label too, so the Playing state reads at a glance.
            SetLabelColor(entity, active ? EditorTheme.Text0 : EditorTheme.TextDisabled);
        }

        if (over && _clicked && !(_isInputSuppressed?.Invoke() ?? false))
            _dispatch(button.Action, state);
    }

    private static void SyncPlayPauseLabel(in Entity button, GameState state)
    {
        if (!button.Has<SimpleButtonComponent>()) return;
        var textEntity = button.Get<SimpleButtonComponent>().TextEntity;
        if (textEntity is not { IsAlive: true } label || !label.Has<DynamicTextComponent>()) return;

        ref var text = ref label.Get<DynamicTextComponent>();
        // While Playing the button offers "Pause"; while Paused it offers "Play". The button was
        // laid out for the wider label, so the swap never moves the row.
        text.TextContent = state.RunMode == RunMode.Play ? "Pause" : "Play";
    }

    private static void SetLabelColor(in Entity button, Color color)
    {
        if (!button.Has<SimpleButtonComponent>()) return;
        var textEntity = button.Get<SimpleButtonComponent>().TextEntity;
        if (textEntity is not { IsAlive: true } label || !label.Has<DynamicTextComponent>()) return;
        label.Get<DynamicTextComponent>().Color = color;
    }

    public override void Dispose()
    {
        _cursorSet.Dispose();
        base.Dispose();
    }
}
