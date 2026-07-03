#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Channel;

/// <summary>
/// The headless editor-op channel driver (Wave 5, contract item 15). It consumes an
/// <see cref="EditorOpPlan"/> and, with <b>no real mouse</b>, drives the real editor systems:
/// it injects cursor world/virtual position + the left-button press/release edges onto the cursor
/// entity (which <c>CursorInputSystem.SkipHardwareRead</c> leaves untouched), drives the transport
/// (<see cref="EditorOpKind.Play"/> / <see cref="EditorOpKind.Pause"/> /
/// <see cref="EditorOpKind.Restart"/> through the bound <see cref="EditorTransport"/>), and fires
/// toolbar actions through a dispatch callback the screen supplies. A scripted press over a gizmo
/// handle, a few drag moves, then a release reproduces exactly the gizmo drag → one undo step the
/// unit tests prove in isolation.
///
/// <para><b>Holds the session open.</b> The driver runs first in the update pipeline and computes the
/// left-button edges (<c>LeftButtonPressed</c>/<c>LeftButtonReleased</c>) from the previous injected
/// state, so the selection / gizmo / toolbar systems downstream this frame see a coherent cursor. It
/// keeps the run alive until the op queue drains plus a configurable tail; only then does it invoke
/// the supplied <c>requestExit</c> — so the input-replay channel's auto-exit-on-drain (which fires when
/// its keyboard commands run out) never kills the editor-op run before its ops + the harness's
/// assertions complete. When both channels are present, <c>requestExit</c> is wired so the game exits
/// only after the editor-op plan finishes.</para>
///
/// <para>It is pure (no <c>GraphicsDevice</c>): a test composes it with the real <c>SelectionSystem</c>
/// / <c>GizmoSystem</c> / <c>ToolbarSystem</c> over a real <c>World</c> and asserts the moved-then-
/// reverted entity + the saved scene, all in-process. The same driver wires into the headless host.</para>
/// </summary>
public sealed class EditorOpReplaySystem : ISystem<GameState>
{
    private readonly EntitySet _cursorSet;
    private readonly List<EditorOp> _ops;
    private readonly int _tailFrames;
    private readonly Action<EditorToolbarAction, GameState>? _dispatch;
    private readonly Action? _requestExit;
    private readonly EditorTransport? _transport;

    private int _frame;
    private int _cursor; // index into the (frame-sorted) ops list
    private int _drainedAtFrame = -1;
    private bool _exitRequested;

    // The injected cursor state we maintain across frames (so we can compute the press/release edges).
    private Vector2 _injectedWorld;
    private Vector2 _injectedVirtual;
    private bool _leftDown;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether the op queue has fully drained (all ops applied + the tail elapsed).</summary>
    public bool IsComplete => _drainedAtFrame >= 0 && _frame > _drainedAtFrame + _tailFrames;

    public EditorOpReplaySystem(
        World world,
        EditorOpPlan plan,
        Action<EditorToolbarAction, GameState>? dispatch = null,
        Action? requestExit = null,
        EditorTransport? transport = null)
    {
        _ops = (plan?.Ops ?? new List<EditorOp>()).OrderBy(o => o.Frame).ToList();
        _tailFrames = Math.Max(0, plan?.TailFrames ?? 1);
        _dispatch = dispatch;
        _requestExit = requestExit;
        _transport = transport;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // 1) Apply every op scheduled for this frame.
        while (_cursor < _ops.Count && _ops[_cursor].Frame <= _frame)
        {
            ApplyOp(state, _ops[_cursor]);
            _cursor++;
        }

        // 2) Stamp the maintained cursor state (with this frame's edges) onto the cursor entity, so the
        //    selection / gizmo / toolbar systems read a coherent injected cursor this frame.
        WriteCursor();

        // 3) Track drain + hold the session open until the tail elapses, then request exit once.
        if (_cursor >= _ops.Count && _drainedAtFrame < 0)
            _drainedAtFrame = _frame;

        if (IsComplete && !_exitRequested)
        {
            _exitRequested = true;
            Logger.Info($"[level-editor] Editor-op plan complete at frame {_frame}. Requesting exit.");
            _requestExit?.Invoke();
        }

        _frame++;
    }

    private void ApplyOp(GameState state, EditorOp op)
    {
        switch (op.Kind)
        {
            case EditorOpKind.MoveCursor:
                _injectedWorld = new Vector2(op.X, op.Y);
                _injectedVirtual = new Vector2(op.X, op.Y);
                break;
            case EditorOpKind.LeftDown:
                _injectedWorld = new Vector2(op.X, op.Y);
                _injectedVirtual = new Vector2(op.X, op.Y);
                _leftDown = true;
                break;
            case EditorOpKind.LeftUp:
                _injectedWorld = new Vector2(op.X, op.Y);
                _injectedVirtual = new Vector2(op.X, op.Y);
                _leftDown = false;
                break;
            case EditorOpKind.Play:
                if (_transport != null) _transport.Play(state);
                else { state.RunMode = RunMode.Play; Logger.Info("[level-editor] Editor-op: Playing (no transport bound)."); }
                break;
            case EditorOpKind.Pause:
                if (_transport != null) _transport.Pause(state);
                else { state.RunMode = RunMode.Edit; Logger.Info("[level-editor] Editor-op: Paused (no transport bound)."); }
                break;
            case EditorOpKind.Restart:
                if (_transport != null) _transport.Restart(state);
                else Logger.Warning("[level-editor] Editor-op: Restart requires a bound EditorTransport — op skipped.");
                break;
            case EditorOpKind.ToolbarAction:
                if (op.Action != null && Enum.TryParse<EditorToolbarAction>(op.Action, ignoreCase: true, out var action))
                    _dispatch?.Invoke(action, state);
                else
                    Logger.Warning($"[level-editor] Editor-op: unknown toolbar action '{op.Action}'.");
                break;
        }
    }

    private void WriteCursor()
    {
        foreach (var entity in _cursorSet.GetEntities())
        {
            ref var input = ref entity.Get<CursorInputComponent>();

            input.PreviousWorldPosition = input.WorldPosition;
            input.PreviousScreenPosition = input.ScreenPosition;

            var prevLeft = input.LeftButton;

            input.WorldPosition = _injectedWorld;
            input.VirtualPosition = _injectedVirtual;
            input.ScreenPosition = _injectedVirtual;
            input.Delta = input.WorldPosition - input.PreviousWorldPosition;
            // The injected cursor addresses world coordinates directly — it is by definition
            // "inside the game viewport", even though the un-mapped ScreenPosition it carries
            // may fall in the chrome margins (CursorPositionSystem runs before this driver and
            // would otherwise leave a stale OutsideViewport=true that muted the injected press).
            input.OutsideViewport = false;

            input.LeftButton = _leftDown;
            input.LeftButtonPressed = _leftDown && !prevLeft;
            input.LeftButtonReleased = !_leftDown && prevLeft;

            entity.NotifyChanged<CursorInputComponent>();
            return; // single cursor
        }
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
