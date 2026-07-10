#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor's Blender-style <b>modal transform</b> owner (UX3-F design §5): <c>G</c>/<c>S</c>/<c>R</c>
/// enter a modal session over the current selection, the mouse drives the transform WITHOUT a button
/// held, and it commits on left-click/Enter or cancels on right-click/Escape — all through the SAME
/// <c>BeginTransaction</c>-coalesced command path as a gizmo drag, so one session = one undo step.
///
/// <para><b>Owns the pointer + keyboard while active.</b> Every active frame it applies the live edit
/// then <b>consumes the cursor's pointer edges</b> (the dialog/menu recipe), so selection, the gizmo,
/// the palette, and camera-nav stand down — in particular the confirm-click never re-picks or clears
/// the selection (pre-mortem #4). It reads its own keys through the injectable
/// <c>Func&lt;KeyboardState&gt;</c> seam; the composing screen ORs <see cref="IsActive"/> into the host
/// keyboard's <c>ShouldSuppressInput</c> and the shortcut system's gate, so no game key or editor chord
/// (incl. a re-triggered G/S/R) fires mid-modal — <b>Escape cancels the modal, not the game or a tool</b>.</para>
///
/// <para><b>The rig composes (UX2-G mapping).</b> <c>G</c> moves the camera rig via a
/// <see cref="TransformEditCommand"/> (its <c>Position</c> is the camera centre); <c>S</c> edits its
/// authored zoom via <see cref="CameraZoomEditCommand"/> (a bigger frustum ⇒ a lower zoom —
/// <c>newZoom = beforeZoom / factor</c>, clamped to the camera-nav range); <c>R</c> is refused for the
/// rig with a status note. A <b>box collider entity</b> likewise refuses <c>R</c> (axis-aligned by the
/// CE model), and a <b>baked product</b> refuses modal entry entirely (it regenerates from its source).</para>
///
/// <para><b>Weave.</b> Register it with the input-owner block, immediately AFTER
/// <c>editor.shortcuts</c> (which ENTERS it) and BEFORE the tools (<c>editor.gizmo</c>) + the draw
/// pipeline's <c>editor.selection</c>, so the consume reaches them all. <c>RunNormally</c> — it self-
/// guards to <see cref="RunMode.Edit"/> (a modal cannot survive into Play).</para>
///
/// <para><b>Headless.</b> The <c>modal:*</c> ops call the public <see cref="Enter"/> /
/// <see cref="SetAxis"/> / <see cref="TypeDigits"/> / <see cref="OpCursor"/> / <see cref="Confirm"/> /
/// <see cref="Cancel"/> — the same methods the keyboard/mouse path drives — so the full flow is
/// exercised with no real mouse or keyboard.</para>
/// </summary>
public sealed class ModalTransformSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly Camera _camera;
    private readonly EditorHistory _history;
    private readonly Func<KeyboardState> _getKeyboardState;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _gizmoStateSet;
    private readonly EntitySet _cursorSet;

    private ModalTransform _modal;   // the pure state machine
    private Entity _target;
    private bool _targetIsRig;       // the target is the camera rig (Scale → zoom, Rotate refused)
    private float _beforeRigZoom;    // the rig's authored zoom at entry (its Scale → zoom edit)
    private KeyboardState _prevKeys;
    private ModalReadout _readout;   // the last computed readout, for the status bar

    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether a modal session is in progress — the screen ORs this into
    /// <c>ShouldSuppressInput</c> and the shortcut gate's <c>modalActive</c>.</summary>
    public bool IsActive => _modal.IsActive;

    /// <summary>The live readout of the active session (meaningful while <see cref="IsActive"/>) — the
    /// status bar's left content while a modal transform runs.</summary>
    public ModalReadout Readout => _readout;

    /// <param name="getKeyboardState">The keyboard seam (default <see cref="Keyboard.GetState"/>).</param>
    public ModalTransformSystem(World world, Camera camera, EditorHistory history,
        Func<KeyboardState>? getKeyboardState = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // A modal cannot survive into Play (and cannot start there). Cancel any in-flight session.
        if (state.RunMode != RunMode.Edit)
        {
            if (_modal.IsActive) Cancel(state);
            _prevKeys = _getKeyboardState();
            return;
        }

        if (!_modal.IsActive)
        {
            _prevKeys = _getKeyboardState(); // keep the edge baseline fresh for the entry frame
            return;
        }

        // The target vanished mid-modal (Delete / undo / a headless op) → cancel like a lost gizmo drag.
        if (!_target.IsAlive)
        {
            Cancel(state);
            _prevKeys = _getKeyboardState();
            return;
        }

        // 1. Keyboard: axis / numeric-buffer edits + confirm(Enter)/cancel(Escape).
        var keys = _getKeyboardState();
        var (kbConfirm, kbCancel) = HandleKeyboard(keys);
        _prevKeys = keys;

        // 2. Mouse + the live edit, then consume so nothing downstream acts on this frame's click.
        if (!TryGetCursorEntity(out var cursorEntity))
        {
            // Keyboard-only path (no cursor entity): honour a keyboard confirm/cancel; the last live
            // edit already stands.
            if (kbCancel) Cancel(state);
            else if (kbConfirm) Confirm(state);
            return;
        }

        ref var cursor = ref cursorEntity.Get<CursorInputComponent>();
        var confirm = kbConfirm || cursor.LeftButtonPressed;
        var cancel = kbCancel || cursor.RightButtonPressed;

        if (cancel)
        {
            ConsumeCursor(ref cursor);
            cursorEntity.NotifyChanged<CursorInputComponent>();
            Cancel(state);
            return;
        }

        // Recompute the live edit from the current cursor (so a confirm captures the exact final amount),
        // then consume the pointer so selection/gizmo/palette/camera-nav stand down (pre-mortem #4).
        ApplyLiveEdit(cursor.WorldPosition);
        ConsumeCursor(ref cursor);
        cursorEntity.NotifyChanged<CursorInputComponent>();

        if (confirm) Confirm(state);
    }

    // ─── Public flow (the modal:* ops + the keyboard/mouse path share these) ─────────────────────────

    /// <summary>Enters a modal session in <paramref name="mode"/> over the current selection. No-op
    /// (returns false, logs the reason) outside Edit, with no selection, while another session/transaction
    /// is open, or Rotate on the camera rig (refused — rig rotation editing is a future wave).</summary>
    public bool Enter(EditorModalMode mode, GameState state)
    {
        if (state.RunMode != RunMode.Edit) return false;
        if (_modal.IsActive) return false;               // one session at a time
        if (_history.InTransaction) return false;        // another coalesced edit (a gizmo drag) is open

        if (!TryGetSelected(out var target) || !target.Has<TransformComponent>())
        {
            Logger.Warning("[level-editor] Modal transform needs a selection.");
            return false;
        }

        // Instance-children guardrail (PF-D): a prefab-owned child is not editable in a scene — refuse
        // entry entirely (so no per-frame edit runs). The instance ROOT is fully editable (not "owned").
        if (PrefabGuards.IsPrefabOwned(target))
        {
            Logger.Warning(PrefabGuards.Refusal("Modal transform"));
            return false;
        }

        // Baked-product guardrail (colliders-as-entities): a boundary's baked segment regenerates from
        // its source, so a modal move/scale would be overwritten — refuse; edit the source instead.
        if (target.Has<BakedProductComponent>())
        {
            Logger.Warning(
                "[level-editor] Modal transform refused: this is a baked product — it regenerates " +
                "from its source. Edit the source (e.g. the boundary) instead.");
            return false;
        }

        _targetIsRig = target.Has<CameraRigComponent>();
        if (mode == EditorModalMode.Rotate && _targetIsRig)
        {
            Logger.Warning("[level-editor] Rotate is disabled for the camera rig.");
            return false;
        }

        // Box colliders are axis-aligned (colliders-as-entities): refuse Rotate like the rig — a box
        // can't rotate (use a polygon collider for a rotated hitbox). Move + Scale work.
        if (mode == EditorModalMode.Rotate && target.Has<BoxColliderComponent>())
        {
            Logger.Warning(
                "[level-editor] Box colliders are axis-aligned and can't be rotated — use a polygon " +
                "collider for a rotated hitbox.");
            return false;
        }

        ref readonly var transform = ref target.Get<TransformComponent>();
        var pivot = transform.WorldPosition;
        var entry = TryGetCursorWorld(out var cursorWorld) ? cursorWorld : pivot;

        _target = target;
        _beforeRigZoom = _targetIsRig ? target.Get<CameraRigComponent>().Zoom : 0f;
        _modal = ModalTransform.Enter(mode, entry, pivot,
            transform.Position, transform.Rotation, transform.Scale, transform.Origin);
        _prevKeys = _getKeyboardState(); // swallow the entry keystroke so no stale edge leaks
        _history.BeginTransaction();
        _readout = _modal.Readout(entry, SnapStep(), RotationSnapStep(), IsRigZoom);
        return true;
    }

    /// <summary>Toggles the axis lock (the <c>modal:axis x|y</c> op / the X/Y keys).</summary>
    public void SetAxis(ModalAxis axis)
    {
        if (_modal.IsActive) _modal.ToggleAxis(axis);
    }

    /// <summary>Appends typed characters to the numeric buffer (the <c>modal:digits &lt;text&gt;</c>
    /// op / the number keys). Digits / <c>-</c> / <c>.</c> only.</summary>
    public void TypeDigits(string text)
    {
        if (!_modal.IsActive || string.IsNullOrEmpty(text)) return;
        foreach (var c in text) _modal.TypeChar(c);
    }

    /// <summary>Removes the last typed character (the Backspace key).</summary>
    public void Backspace()
    {
        if (_modal.IsActive) _modal.Backspace();
    }

    /// <summary>Headless cursor motion (the <c>modal:cursor &lt;dx&gt; &lt;dy&gt;</c> op): moves the cursor
    /// to <c>entry + (dx, dy)</c> and applies the live edit, so a scripted flow needs no real mouse and no
    /// intervening frame. Also writes the cursor entity so the next <see cref="Update"/> reads the same
    /// point.</summary>
    public void OpCursor(float dx, float dy)
    {
        if (!_modal.IsActive) return;
        var world = _modal.EntryCursorWorld + new Vector2(dx, dy);
        if (TryGetCursorEntity(out var cursorEntity))
        {
            cursorEntity.Get<CursorInputComponent>().WorldPosition = world;
            cursorEntity.NotifyChanged<CursorInputComponent>();
        }
        ApplyLiveEdit(world);
    }

    /// <summary>Commits the session as ONE undo step (left-click / Enter / the <c>modal:confirm</c> op).</summary>
    public void Confirm(GameState state)
    {
        if (!_modal.IsActive) return;
        if (_history.InTransaction) _history.CommitTransaction();
        Reset();
    }

    /// <summary>Cancels the session, reverting the live edit (right-click / Escape / the
    /// <c>modal:cancel</c> op).</summary>
    public void Cancel(GameState state)
    {
        if (!_modal.IsActive) return;
        if (_history.InTransaction) _history.CancelTransaction();
        Reset();
    }

    // ─── internals ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Whether the active session is the rig's Scale → zoom edit (drives the "Zoom" readout).</summary>
    private bool IsRigZoom => _targetIsRig && _modal.Mode == EditorModalMode.Scale;

    private (bool confirm, bool cancel) HandleKeyboard(KeyboardState keys)
    {
        if (Pressed(keys, Keys.Escape)) return (false, true); // Escape cancels — modal wins over tool-disarm
        var confirm = Pressed(keys, Keys.Enter);

        if (Pressed(keys, Keys.X)) _modal.ToggleAxis(ModalAxis.X);
        if (Pressed(keys, Keys.Y)) _modal.ToggleAxis(ModalAxis.Y);
        if (Pressed(keys, Keys.Back)) _modal.Backspace();

        for (var d = 0; d <= 9; d++)
        {
            if (Pressed(keys, Keys.D0 + d) || Pressed(keys, Keys.NumPad0 + d))
                _modal.TypeChar((char)('0' + d));
        }
        if (Pressed(keys, Keys.OemMinus) || Pressed(keys, Keys.Subtract)) _modal.TypeChar('-');
        if (Pressed(keys, Keys.OemPeriod) || Pressed(keys, Keys.Decimal)) _modal.TypeChar('.');

        return (confirm, false);
    }

    private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);

    /// <summary>Applies the current live edit through the coalescing transaction — a
    /// <see cref="TransformEditCommand"/> for an ordinary entity (or the rig's Grab), or a
    /// <see cref="CameraZoomEditCommand"/> for the rig's Scale (zoom, clamped to the camera-nav range).
    /// Recomputes from the immutable drag-start state each frame, so undo walks back to the pre-modal
    /// transform in one step.</summary>
    private void ApplyLiveEdit(Vector2 cursorWorld)
    {
        if (!_target.IsAlive) return;

        if (IsRigZoom)
        {
            var factor = _modal.UniformScaleFactor(cursorWorld);
            var zoom = MathHelper.Clamp(_beforeRigZoom / factor,
                CameraNavSystem.DefaultMinZoom, CameraNavSystem.DefaultMaxZoom);
            _history.Push(CameraZoomEditCommand.FromCurrent(_target, zoom));
            _readout = _modal.Readout(cursorWorld, SnapStep(), RotationSnapStep(), isRig: true);
            return;
        }

        if (!_target.Has<TransformComponent>()) return;
        var (position, rotation, scale, origin) = _modal.Result(cursorWorld, SnapStep(), RotationSnapStep());
        _history.Push(TransformEditCommand.FromCurrent(_target, position, rotation, scale, origin));
        _readout = _modal.Readout(cursorWorld, SnapStep(), RotationSnapStep(), isRig: false);
    }

    private void Reset()
    {
        _modal = default;
        _target = default;
        _targetIsRig = false;
        _beforeRigZoom = 0f;
    }

    /// <summary>Clears the cursor's pointer edges + button levels for this frame — the modal owns the
    /// pointer while active (the dialog/menu recipe).</summary>
    private static void ConsumeCursor(ref CursorInputComponent cursor)
    {
        cursor.LeftButtonPressed = cursor.RightButtonPressed = cursor.MiddleButtonPressed = false;
        cursor.LeftButtonReleased = cursor.RightButtonReleased = cursor.MiddleButtonReleased = false;
        cursor.LeftButton = cursor.RightButton = cursor.MiddleButton = false;
        cursor.ScrollWheelDelta = 0;
    }

    private float SnapStep()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
        {
            ref readonly var g = ref e.Get<GizmoStateComponent>();
            return g.SnapEnabled && g.GridStep > 0f ? g.GridStep : 0f;
        }
        return 0f;
    }

    private float RotationSnapStep()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
        {
            ref readonly var g = ref e.Get<GizmoStateComponent>();
            return g.SnapEnabled && g.RotationStepRadians > 0f ? g.RotationStepRadians : 0f;
        }
        return 0f;
    }

    private bool TryGetSelected(out Entity target)
    {
        foreach (var e in _selectedSet.GetEntities())
        {
            if (e.IsAlive && e.Has<TransformComponent>())
            {
                target = e;
                return true;
            }
        }
        target = default;
        return false;
    }

    private bool TryGetCursorEntity(out Entity cursorEntity)
    {
        foreach (var e in _cursorSet.GetEntities())
        {
            cursorEntity = e;
            return true;
        }
        cursorEntity = default;
        return false;
    }

    private bool TryGetCursorWorld(out Vector2 world)
    {
        foreach (var e in _cursorSet.GetEntities())
        {
            world = e.Get<CursorInputComponent>().WorldPosition;
            return true;
        }
        world = default;
        return false;
    }

    public void Dispose()
    {
        _selectedSet.Dispose();
        _gizmoStateSet.Dispose();
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
