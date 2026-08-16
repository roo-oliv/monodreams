#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Debug.Input;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System.Debug;

/// <summary>
/// Drives a <see cref="PointerReplayPlan"/>: a scripted mouse for mouse-first games. It is the
/// pointer sibling of <c>InputReplaySystem</c> — file-gated, deterministic (frame-counted),
/// fully logged, auto-exiting when the plan drains — and it exists because a named-action replay
/// can drive a platformer but has nothing to say to a business sim, a card game or an editor.
///
/// <para><b>It injects into the real cursor path, it does not simulate one.</b> Each frame the
/// driver stamps the plan's state onto the screen's <see cref="CursorInputComponent"/> — position,
/// button levels AND the press/release edges, the scroll accumulator — and places the cursor entity
/// through <c>Cursor.ApplyPose</c>, the same pose rule <c>CursorPositionSystem</c> uses for a real
/// mouse. Everything downstream (hover, picking, UI focus, buttons, scroll views, the editor's own
/// tools) then runs exactly as it does under a hand on the mouse. The screen that composes this
/// driver therefore stands the hardware path down —
/// <c>CursorInputSystem.SkipHardwareRead = true</c> and
/// <c>CursorPositionSystem.SkipDerivation = true</c> — and registers the driver immediately after
/// the cursor-input stage, so every consumer sees the injected frame the same frame.</para>
///
/// <para><b>Coordinates are authoring space</b> (virtual resolution), never window pixels: the
/// driver derives world coordinates through the screen's <c>Camera</c>. That is also what
/// makes the driver work on a headless host, whose 1x1 backbuffer has no meaningful window-to-virtual
/// mapping to invert. The one cursor field that is NOT in that space is <c>ScreenPosition</c>, which
/// is backbuffer pixels by contract, so the driver maps the authored point forward through the
/// screen's <c>ViewportManager</c> to fill it. A consequence worth stating: a plan can only address
/// points inside the game viewport, so the editor shell's chrome — a toolbar or panel living in the
/// inset margins, hit-tested in screen space — is not reachable from a pointer plan. Scripting the
/// editor's own controls is <c>EditorOpReplaySystem</c>'s job, by action name.</para>
///
/// <para><b>The plan is per-screen.</b> A driver belongs to the screen that composed it; a screen
/// transition disposes it mid-plan (which is the normal end of a scenario that navigates away — the
/// destination screen's own driver, or its input replay, owns what happens next).</para>
/// </summary>
public sealed class PointerReplaySystem : ISystem<GameState>
{
    /// <summary>How many recent log messages the <c>waitUntil</c>-on-log predicate can look back over.
    /// Bounded so a long run cannot grow the ring without limit.</summary>
    private const int LogRingCapacity = 512;

    /// <summary>
    /// The prefix every line this driver writes carries — and, deliberately, the filter its log ring
    /// applies. A <c>waitUntil log</c> must be satisfied by the GAME's output, never by the driver's
    /// own narration of the wait: the announcement <c>waitUntil log="level ready"</c> contains
    /// "level ready", so recording it would make every such predicate true on the frame it starts.
    /// </summary>
    private const string Tag = "[pointer]";

    private readonly EntitySet _cursors;
    private readonly EntitySet _named;
    // Fully qualified: `Camera` alone binds to the camera MODULE's namespace from inside
    // `MonoDreams.System.*` (the same reason `CursorPositionSystem` spells it out).
    private readonly MonoDreams.Component.Camera? _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly List<PointerCommand> _commands;
    private readonly int _tailFrames;
    private readonly Action? _requestExit;

    // ── the injected pointer state, maintained across frames ────────────────────────────────
    private Vector2 _position;
    private bool _left, _right, _middle;
    private bool _prevLeft, _prevRight, _prevMiddle;
    private int _scrollValue;
    private int _scrollDelta;                       // one-frame pulse
    private Keys[] _keys = Array.Empty<Keys>();     // one-frame pulse

    // ── plan progress ───────────────────────────────────────────────────────────────────────
    private int _frame;
    private int _index;        // the command being executed
    private int _stepFrame;    // frames already spent on that command
    private int _drainedAtFrame = -1;
    private bool _exitRequested;

    // ── the log tap backing the waitUntil-on-log predicate ──────────────────────────────────
    private readonly object _logLock = new();
    private readonly Queue<(long Seq, string Message)> _logRing = new();

    /// <summary>Monotonic sequence stamped on every line the tap accepts, so a line stays identifiable
    /// after the ring has evicted its neighbours.</summary>
    private long _logSeq;

    /// <summary>The oldest line sequence a <c>waitUntil log</c> may still match. A satisfied log wait
    /// CONSUMES the line it matched by moving this past it — see <see cref="LogSeen"/>.</summary>
    private long _logWatermark;

    private bool _sinkInstalled;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether every command has run and the tail has elapsed.</summary>
    public bool IsComplete => _drainedAtFrame >= 0 && _frame > _drainedAtFrame + _tailFrames;

    /// <summary>Frames executed so far — the driver's clock, exposed for tests.</summary>
    public int Frame => _frame;

    /// <summary>
    /// The keyboard this driver synthesizes for <see cref="PointerCommandKind.Type"/>. Hand it to any
    /// system that takes the repo's <c>Func&lt;KeyboardState&gt;</c> keyboard seam (e.g.
    /// <c>new TextInputSystem(world) { KeyboardStateProvider = pointer.ReadKeyboard }</c>) and typed
    /// text reaches it through that system's real per-frame key diff. Screens that script no typing
    /// simply never wire it.
    /// </summary>
    public KeyboardState ReadKeyboard() => new(_keys);

    /// <summary>
    /// Builds a driver over <paramref name="plan"/>. <paramref name="camera"/> derives world
    /// coordinates from the authored virtual ones (null → world == authoring space, which is what a
    /// unit test without a camera wants). <paramref name="viewportManager"/> maps those same authored
    /// virtual coordinates forward into the backbuffer pixels
    /// <see cref="CursorInputComponent.ScreenPosition"/> is contractually in (null → the two spaces are
    /// treated as one, again the unit-test case); pass the screen's real manager or an injected pointer
    /// reports a screen position in the wrong space on any run where the two differ (a letterboxed
    /// window, an editor inset, a Retina backbuffer). <paramref name="requestExit"/> is invoked once,
    /// after the plan drains plus <see cref="PointerReplayPlan.TailFrames"/> — omit it and the driver
    /// just stops driving (a host that owns its own exit, like the frame-capped Demos host).
    /// </summary>
    public PointerReplaySystem(World world, PointerReplayPlan plan, MonoDreams.Component.Camera? camera = null,
        ViewportManager? viewportManager = null, Action? requestExit = null)
    {
        _commands = plan?.Commands ?? new List<PointerCommand>();
        _tailFrames = Math.Max(0, plan?.TailFrames ?? 0);
        _camera = camera;
        _viewportManager = viewportManager;
        _requestExit = requestExit;
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
        _named = world.GetEntities().With<EntityInfoComponent>().AsSet();

        Logger.LineSink = OnLogLine;
        _sinkInstalled = true;

        Logger.Info($"{Tag} Plan loaded: \"{plan?.Description}\" ({_commands.Count} commands, " +
                    $"tail {_tailFrames} frames)");
    }

    /// <summary>
    /// Loads <c>pointer_replay.json</c> from <paramref name="debugDirectory"/> and builds a driver for
    /// it, or returns <c>null</c> when the file is absent/unparseable/empty — the file gate. A screen
    /// wires it in one line and a run without the file is byte-identical to one before the driver
    /// existed.
    /// </summary>
    public static PointerReplaySystem? TryLoad(string debugDirectory, World world,
        MonoDreams.Component.Camera? camera = null,
        ViewportManager? viewportManager = null,
        Action? requestExit = null)
    {
        var plan = PointerReplayPlan.TryLoad(debugDirectory);
        if (plan == null) return null;
        if (plan.Commands == null || plan.Commands.Count == 0)
        {
            Logger.Warning($"{Tag} {PointerReplayPlan.FileName} has no commands. Pointer replay disabled.");
            return null;
        }

        return new PointerReplaySystem(world, plan, camera, viewportManager, requestExit);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // One-frame pulses default to "nothing happened"; a command below may re-arm them.
        _scrollDelta = 0;
        _keys = Array.Empty<Keys>();

        if (_index < _commands.Count)
        {
            var command = _commands[_index];
            if (_stepFrame == 0) LogStart(command, state);

            if (Step(command))
            {
                _index++;
                _stepFrame = 0;
            }
            else
            {
                _stepFrame++;
            }
        }

        // Stamp the maintained state onto the cursor entity so every downstream consumer reads a
        // coherent injected pointer THIS frame.
        WriteCursor();

        if (_index >= _commands.Count && _drainedAtFrame < 0)
        {
            _drainedAtFrame = _frame;
            Logger.Info($"{Tag} Plan drained at frame {_frame}.");
        }

        if (IsComplete && !_exitRequested)
        {
            _exitRequested = true;
            Logger.Info($"{Tag} Plan complete at frame {_frame}. Requesting exit.");
            _requestExit?.Invoke();
        }

        _frame++;
    }

    /// <summary>Executes one frame of <paramref name="command"/>; returns whether it is finished.</summary>
    private bool Step(PointerCommand command)
    {
        switch (command.Kind)
        {
            case PointerCommandKind.Move:
                MoveTo(command);
                return true;

            case PointerCommandKind.Click:
            {
                var hold = Math.Max(1, command.Hold);
                if (_stepFrame == 0) MoveTo(command);
                SetButton(command.Button, _stepFrame < hold);
                return _stepFrame >= hold;
            }

            case PointerCommandKind.Wheel:
                _scrollDelta = command.Delta;
                _scrollValue += command.Delta;
                return true;

            case PointerCommandKind.Type:
            {
                var text = command.Text ?? string.Empty;
                if (text.Length == 0) return true;
                // Two frames per character: a press frame then a gap frame, so an edge-triggered
                // reader ("keys down now that were up last frame") sees repeats as separate presses.
                if (_stepFrame % 2 == 0)
                {
                    var c = text[_stepFrame / 2];
                    if (TryMapCharacter(c, out var key)) _keys = new[] { key };
                    else Logger.Warning($"{Tag} type: character '{c}' has no key mapping — skipped.");
                }
                return _stepFrame >= text.Length * 2 - 1;
            }

            case PointerCommandKind.WaitUntil:
                if (IsSatisfied(command)) return true;
                if (_stepFrame + 1 >= Math.Max(1, command.TimeoutFrames))
                {
                    Logger.Error($"{Tag} waitUntil {Describe(command)} TIMED OUT after " +
                                 $"{command.TimeoutFrames} frames — continuing.");
                    return true;
                }
                return false;

            case PointerCommandKind.Label:
            default:
                return true; // the marker was already written by LogStart
        }
    }

    private void MoveTo(PointerCommand command)
    {
        if (command.X.HasValue) _position.X = command.X.Value;
        if (command.Y.HasValue) _position.Y = command.Y.Value;
    }

    private void SetButton(PointerButton button, bool down)
    {
        switch (button)
        {
            case PointerButton.Right: _right = down; break;
            case PointerButton.Middle: _middle = down; break;
            default: _left = down; break;
        }
    }

    private bool IsSatisfied(PointerCommand command)
    {
        if (command.Frames.HasValue) return _stepFrame + 1 >= command.Frames.Value;
        if (command.Entity != null) return EntityExists(command.Entity);
        if (command.Log != null) return LogSeen(command.Log);

        Logger.Warning($"{Tag} waitUntil with no predicate (entity/log/frames) — treated as satisfied.");
        return true;
    }

    private bool EntityExists(string identifier)
    {
        foreach (var entity in _named.GetEntities())
        {
            var info = entity.Get<EntityInfoComponent>();
            if (info == null) continue;
            if (identifier.Equals(info.Type, StringComparison.OrdinalIgnoreCase)
                || identifier.Equals(info.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether an unconsumed log line contains <paramref name="substring"/> — and, when one does,
    /// CONSUMES it by moving the watermark past it so no later wait can be satisfied by the same line.
    ///
    /// <para>The watermark is what makes a repeated wait mean what it reads. In
    /// <c>[click Save, waitUntil log="Scene saved", click Save, waitUntil log="Scene saved"]</c> an
    /// unwatermarked scan makes the second wait pass instantly on the FIRST save's line, so the second
    /// click fires ungated — the exact race <c>waitUntil</c> exists to remove.</para>
    ///
    /// <para>It is deliberately NOT reset to "now" when a wait starts: the line a wait gates on is
    /// normally emitted by the command before it, downstream of this driver in a frame that has
    /// already finished by the time the wait first runs. A start-of-wait snapshot would therefore skip
    /// exactly the line the script is waiting for and every such wait would sit until its timeout.</para>
    /// </summary>
    private bool LogSeen(string substring)
    {
        lock (_logLock)
        {
            foreach (var (seq, message) in _logRing)
            {
                if (seq < _logWatermark) continue;
                if (!message.Contains(substring, StringComparison.OrdinalIgnoreCase)) continue;
                _logWatermark = seq + 1;
                return true;
            }
        }

        return false;
    }

    /// <summary>The <c>Logger.LineSink</c> plug. Thread-safe (the logger is written from background
    /// work too) and it never logs — re-entering <c>Logger.Write</c> from here would not terminate.</summary>
    private void OnLogLine(LogLevel level, string message)
    {
        // The driver's own lines are narration, not game output — see the Tag doc.
        if (message.StartsWith(Tag, StringComparison.Ordinal)) return;

        lock (_logLock)
        {
            _logRing.Enqueue((_logSeq++, message));
            while (_logRing.Count > LogRingCapacity) _logRing.Dequeue();
        }
    }

    private void WriteCursor()
    {
        foreach (var entity in _cursors.GetEntities())
        {
            var previousWorld = entity.Get<CursorInputComponent>().WorldPosition;
            var previousScreen = entity.Get<CursorInputComponent>().ScreenPosition;

            // The pose rule the real cursor pipeline uses — same virtual→world derivation, same
            // per-render-target transform placement.
            var world = _camera?.VirtualScreenToWorld(_position) ?? _position;
            MonoDreams.Cursor.Cursor.ApplyPose(entity, _position, world);

            ref var input = ref entity.Get<CursorInputComponent>();
            input.PreviousWorldPosition = previousWorld;
            input.PreviousScreenPosition = previousScreen;
            // ScreenPosition is BACKBUFFER PIXELS by contract — CursorInputSystem scales the raw OS
            // mouse by DevicePixelRatio precisely to keep it so, and the editor's chrome hit-tests read
            // the field raw — while a plan authors in virtual space. So map the authored point FORWARD
            // through the very letterbox/inset rectangle CursorPositionSystem inverts, rather than
            // writing an authoring-space number into a device-pixel field (which lands a chrome
            // hit-test at half the intended point the moment DevicePixelRatio is 2). With no viewport
            // manager (a unit test) the two spaces coincide and the map is the identity.
            input.ScreenPosition = _viewportManager?.ScaleVirtualToScreenCoordinates(_position) ?? _position;
            // The injected pointer addresses authoring space directly, so it is by definition inside
            // the viewport — a stale OutsideViewport = true would mute every world-space click.
            input.OutsideViewport = false;
            // Screen-space delta, the same quantity CursorInputSystem writes (ScreenPosition minus
            // last frame's) — consumers that test "did the pointer move" must read one meaning.
            input.Delta = input.ScreenPosition - previousScreen;

            // Edges derive from the DRIVER's own previous levels, never from the mutable level fields
            // on the component: a consumer is free to clear those to consume a click (an editor modal
            // does exactly that) and the next frame's edges must still be correct.
            input.LeftButton = _left;
            input.RightButton = _right;
            input.MiddleButton = _middle;
            input.LeftButtonPressed = _left && !_prevLeft;
            input.RightButtonPressed = _right && !_prevRight;
            input.MiddleButtonPressed = _middle && !_prevMiddle;
            input.LeftButtonReleased = !_left && _prevLeft;
            input.RightButtonReleased = !_right && _prevRight;
            input.MiddleButtonReleased = !_middle && _prevMiddle;
            input.PreviousLeftButton = _left;
            input.PreviousRightButton = _right;
            input.PreviousMiddleButton = _middle;

            input.ScrollWheelValue = _scrollValue;
            input.ScrollWheelDelta = _scrollDelta;

            entity.NotifyChanged<CursorInputComponent>();
            if (entity.Has<TransformComponent>()) entity.NotifyChanged<TransformComponent>();
            break; // single cursor
        }

        _prevLeft = _left;
        _prevRight = _right;
        _prevMiddle = _middle;
    }

    private void LogStart(PointerCommand command, GameState state)
    {
        switch (command.Kind)
        {
            case PointerCommandKind.Label:
                Logger.Info($"{Tag} label: {command.Text} (frame {_frame}, GT {state.TotalTime:F2})");
                break;
            case PointerCommandKind.Move:
                Logger.Info($"{Tag} move to ({(command.X ?? _position.X):F0}, " +
                            $"{(command.Y ?? _position.Y):F0}) at frame {_frame}");
                break;
            case PointerCommandKind.Click:
                Logger.Info($"{Tag} click {command.Button} at ({(command.X ?? _position.X):F0}, " +
                            $"{(command.Y ?? _position.Y):F0}) at frame {_frame}");
                break;
            case PointerCommandKind.Wheel:
                Logger.Info($"{Tag} wheel {command.Delta} at frame {_frame}");
                break;
            case PointerCommandKind.Type:
                Logger.Info($"{Tag} type \"{command.Text}\" at frame {_frame}");
                break;
            case PointerCommandKind.WaitUntil:
                Logger.Info($"{Tag} waitUntil {Describe(command)} at frame {_frame}");
                break;
        }
    }

    private static string Describe(PointerCommand command) =>
        command.Frames.HasValue ? $"frames={command.Frames.Value}"
        : command.Entity != null ? $"entity=\"{command.Entity}\""
        : command.Log != null ? $"log=\"{command.Log}\""
        : "<no predicate>";

    /// <summary>Maps a character onto the key that produces it, matching the lowercase-only mapping
    /// the <c>ui</c> module's text field reads back (<c>a-z</c>, <c>0-9</c>, space).</summary>
    private static bool TryMapCharacter(char c, out Keys key)
    {
        var lower = char.ToLowerInvariant(c);
        if (lower is >= 'a' and <= 'z') { key = Keys.A + (lower - 'a'); return true; }
        if (lower is >= '0' and <= '9') { key = Keys.D0 + (lower - '0'); return true; }
        if (lower == ' ') { key = Keys.Space; return true; }
        key = Keys.None;
        return false;
    }

    public void Dispose()
    {
        if (_sinkInstalled)
        {
            // Single-owner socket: this driver installed it, this driver removes it.
            Logger.LineSink = null;
            _sinkInstalled = false;
        }

        _cursors.Dispose();
        _named.Dispose();
        GC.SuppressFinalize(this);
    }
}
