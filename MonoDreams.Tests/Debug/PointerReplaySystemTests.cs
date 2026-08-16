using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Debug.Input;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoDreams.System.Debug;
using MonoDreams.UI;
using Xunit;
using CursorFactory = MonoDreams.Cursor.Cursor;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.Debug;

/// <summary>
/// Protects the debug premises "<c>PointerReplaySystem</c> injects through the real cursor path"
/// and "a pointer plan is frame-counted and drains into an exit".
///
/// <para>Everything here runs against a real <see cref="World"/> and the real cursor factory with no
/// <c>GraphicsDevice</c>: the driver is pure (it writes components and reads a camera matrix), which
/// is exactly what makes a scripted pointer testable in-process as well as in a spawned game.</para>
/// </summary>
public class PointerReplaySystemTests
{
    private static GameState Frame() => new(new GameTime());

    private static Entity MakeCursor(World world, RenderTargetID target = RenderTargetID.HUD) =>
        CursorFactory.CreateMesh(world, Triangle(), target);

    private static MeshData Triangle() => new(
        [
            new VertexPositionColor(new Vector3(0, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(8, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(0, 8, 0), Color.White),
        ],
        [0, 1, 2]);

    private static PointerReplayPlan Plan(int tailFrames, params PointerCommand[] commands) =>
        new() { Description = "test", TailFrames = tailFrames, Commands = [.. commands] };

    /// <summary>Runs the driver for <paramref name="frames"/> frames against one shared GameState.</summary>
    private static void Run(PointerReplaySystem driver, int frames, Action<int>? afterFrame = null)
    {
        var state = Frame();
        for (var i = 0; i < frames; i++)
        {
            driver.Update(state);
            afterFrame?.Invoke(i);
        }
    }

    // ── position ────────────────────────────────────────────────────────────────────────────

    /// <summary>A <c>move</c> lands the authored authoring-space point on the cursor's virtual
    /// position, derives the world position through the camera (not a copy of the virtual one), and
    /// places the HUD cursor's transform at the virtual point — i.e. it goes through the same pose
    /// rule the real <c>CursorPositionSystem</c> uses.</summary>
    [Fact]
    public void Move_WritesVirtualWorldAndTransform_ThroughTheRealPoseRule()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var camera = new GameCamera(1920, 1080) { Position = Vector2.Zero };

        using var driver = new PointerReplaySystem(world,
            Plan(0, new PointerCommand { Kind = PointerCommandKind.Move, X = 960, Y = 610 }), camera);

        Run(driver, 1);

        ref readonly var input = ref cursor.Get<CursorInputComponent>();
        Assert.Equal(new Vector2(960, 610), input.VirtualPosition);
        // Camera at the origin over a 1920x1080 virtual surface: world = virtual - half-extent.
        Assert.Equal(camera.VirtualScreenToWorld(new Vector2(960, 610)), input.WorldPosition);
        Assert.NotEqual(input.VirtualPosition, input.WorldPosition);
        // HUD target renders in virtual space, so that is where the transform goes.
        Assert.Equal(new Vector2(960, 610), cursor.Get<TransformComponent>().Position);
        Assert.False(input.OutsideViewport);
    }

    /// <summary>A Main-target cursor is placed in WORLD space instead — the same per-target branch
    /// <c>CursorPositionSystem</c> applies, which is why both call one shared helper.</summary>
    [Fact]
    public void Move_OnAMainTargetCursor_PlacesTheTransformInWorldSpace()
    {
        using var world = new World();
        var cursor = MakeCursor(world, RenderTargetID.Main);
        var camera = new GameCamera(1920, 1080) { Position = Vector2.Zero };

        using var driver = new PointerReplaySystem(world,
            Plan(0, new PointerCommand { Kind = PointerCommandKind.Move, X = 100, Y = 200 }), camera);

        Run(driver, 1);

        Assert.Equal(camera.VirtualScreenToWorld(new Vector2(100, 200)),
            cursor.Get<TransformComponent>().Position);
    }

    // ── buttons ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The load-bearing edge contract: a <c>click</c> produces a press edge on one frame and
    /// a release edge on a later one (a consumer acting on either observes it), and both edges are
    /// gone the frame after — a one-frame pulse, exactly like the hardware path.</summary>
    [Fact]
    public void Click_ProducesAPressEdgeThenAReleaseEdge_ThenNothing()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(4,
            new PointerCommand { Kind = PointerCommandKind.Click, X = 10, Y = 20 }));

        var pressed = new List<bool>();
        var released = new List<bool>();
        var down = new List<bool>();
        Run(driver, 4, _ =>
        {
            ref readonly var i = ref cursor.Get<CursorInputComponent>();
            pressed.Add(i.LeftButtonPressed);
            released.Add(i.LeftButtonReleased);
            down.Add(i.LeftButton);
        });

        Assert.Equal(new[] { true, false, false, false }, pressed);
        Assert.Equal(new[] { false, true, false, false }, released);
        Assert.Equal(new[] { true, false, false, false }, down);
        // The click carried its own move.
        Assert.Equal(new Vector2(10, 20), cursor.Get<CursorInputComponent>().VirtualPosition);
    }

    /// <summary>A held click keeps the button down for <c>hold</c> frames before the release edge, so
    /// a drag-style consumer sees a real press-hold-release rather than a one-frame blip.</summary>
    [Fact]
    public void Click_WithHold_KeepsTheButtonDownForThatManyFrames()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.Click, Hold = 3 }));

        var down = new List<bool>();
        Run(driver, 5, _ => down.Add(cursor.Get<CursorInputComponent>().LeftButton));

        Assert.Equal(new[] { true, true, true, false, false }, down);
    }

    /// <summary>The button a command names is the button that moves; the others stay untouched.</summary>
    [Fact]
    public void Click_Right_DrivesTheRightButtonOnly()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.Click, Button = PointerButton.Right }));

        Run(driver, 1);

        ref readonly var input = ref cursor.Get<CursorInputComponent>();
        Assert.True(input.RightButtonPressed);
        Assert.False(input.LeftButtonPressed);
        Assert.False(input.MiddleButtonPressed);
    }

    /// <summary>The wheel is a one-frame delta on top of a running accumulator — the shape every
    /// consumer expects (<c>ScrollWheelDelta</c> is the edge, <c>ScrollWheelValue</c> the level).</summary>
    [Fact]
    public void Wheel_PulsesTheDeltaForOneFrame_AndAccumulatesTheValue()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.Wheel, Delta = 120 },
            new PointerCommand { Kind = PointerCommandKind.Wheel, Delta = -240 }));

        var deltas = new List<int>();
        var values = new List<int>();
        Run(driver, 3, _ =>
        {
            ref readonly var i = ref cursor.Get<CursorInputComponent>();
            deltas.Add(i.ScrollWheelDelta);
            values.Add(i.ScrollWheelValue);
        });

        Assert.Equal(new[] { 120, -240, 0 }, deltas);
        Assert.Equal(new[] { 120, -120, -120 }, values);
    }

    // ── waitUntil ───────────────────────────────────────────────────────────────────────────

    /// <summary>The predicate that keeps a script from racing the game: the click after a
    /// <c>waitUntil entity</c> does not fire until an entity with that <c>EntityInfoComponent</c>
    /// appears — and fires on the very next frame once it does.</summary>
    [Fact]
    public void WaitUntilEntity_BlocksTheScriptUntilTheEntityExists()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.WaitUntil, Entity = "Dialog", TimeoutFrames = 100 },
            new PointerCommand { Kind = PointerCommandKind.Click }));

        var state = Frame();
        for (var i = 0; i < 5; i++) driver.Update(state);
        Assert.False(cursor.Get<CursorInputComponent>().LeftButton); // still waiting

        var dialog = world.CreateEntity();
        dialog.Set(new EntityInfoComponent("Dialog"));

        driver.Update(state); // the wait is satisfied on this frame
        driver.Update(state); // the click runs on the next
        Assert.True(cursor.Get<CursorInputComponent>().LeftButtonPressed);
    }

    /// <summary>A <c>waitUntil</c> that never comes true gives up after its timeout and moves on, so a
    /// stuck script still ends the run (with an ERROR line naming the predicate) instead of hanging
    /// it — the difference between a diagnosable failure and a CI timeout.</summary>
    [Fact]
    public void WaitUntilEntity_TimesOut_AndTheScriptContinues()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.WaitUntil, Entity = "NeverAppears", TimeoutFrames = 3 },
            new PointerCommand { Kind = PointerCommandKind.Click }));

        Run(driver, 4);

        Assert.True(cursor.Get<CursorInputComponent>().LeftButtonPressed);
    }

    /// <summary>The log predicate reads the driver's <c>Logger.LineSink</c> tap: a line written after
    /// the driver was built satisfies it. (The driver's own stage markers go through the same tap,
    /// which is what lets a script wait on a stage it just announced.)</summary>
    [Fact]
    public void WaitUntilLog_IsSatisfiedByALineWrittenWhileTheDriverRuns()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.WaitUntil, Log = "level ready", TimeoutFrames = 100 },
            new PointerCommand { Kind = PointerCommandKind.Click }));

        var state = Frame();
        for (var i = 0; i < 3; i++) driver.Update(state);
        Assert.False(cursor.Get<CursorInputComponent>().LeftButton);

        Logger.Info("The Level ready marker."); // case-insensitive substring match

        driver.Update(state);
        driver.Update(state);
        Assert.True(cursor.Get<CursorInputComponent>().LeftButtonPressed);
    }

    /// <summary>The driver owns the <c>Logger.LineSink</c> socket while it lives and releases it on
    /// dispose — a screen transition must not leave a dead driver taping every log line for the rest
    /// of the process.</summary>
    [Fact]
    public void Dispose_ReleasesTheLoggerTap()
    {
        using var world = new World();
        MakeCursor(world);
        var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.Label, Text = "x" }));
        Assert.NotNull(Logger.LineSink);

        driver.Dispose();

        Assert.Null(Logger.LineSink);
    }

    // ── drain / exit ────────────────────────────────────────────────────────────────────────

    /// <summary>A plan that runs out asks the host to exit exactly once, after its tail frames — the
    /// auto-exit-on-drain contract it shares with the input replay, and what makes an unattended
    /// scripted run terminate on its own.</summary>
    [Fact]
    public void DrainedPlan_RequestsExitOnce_AfterTheTail()
    {
        using var world = new World();
        MakeCursor(world);
        var exits = 0;
        using var driver = new PointerReplaySystem(world,
            Plan(2, new PointerCommand { Kind = PointerCommandKind.Label, Text = "only" }),
            camera: null, requestExit: () => exits++);

        // frame 0 runs the label; the plan drains that frame; the tail is 2 more frames.
        Run(driver, 3);
        Assert.Equal(0, exits);

        Run(driver, 3);
        Assert.Equal(1, exits);
        Assert.True(driver.IsComplete);
    }

    // ── type ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A <c>type</c> command reaches a real <c>TextInputSystem</c> through its keyboard seam,
    /// so the text lands via the field's own per-frame key diff — mask, caret and
    /// <c>TextInputChanged</c> included — rather than by writing the field's value behind its back.
    /// The doubled cadence is what makes a repeated character ("ll") arrive as two presses.</summary>
    [Fact]
    public void Type_ReachesARealTextInputSystem_ThroughTheKeyboardSeam()
    {
        using var world = new World();
        MakeCursor(world);

        var field = world.CreateEntity();
        field.Set(new TextInputComponent { Text = string.Empty, MaxLength = 32, Focused = true });

        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.Type, Text = "hello 42" }));
        using var textInput = new TextInputSystem(world) { KeyboardStateProvider = driver.ReadKeyboard };

        var state = Frame();
        for (var i = 0; i < 40; i++)
        {
            driver.Update(state);
            textInput.Update(state);
        }

        Assert.Equal("hello 42", field.Get<TextInputComponent>().Text);
    }

    /// <summary>The synthesized keyboard is a one-frame pulse: the key is down on the press frame and
    /// up on the gap frame, which is precisely what an edge-triggered reader needs.</summary>
    [Fact]
    public void Type_SynthesizesOneKeyPerPressFrame_WithAGapBetween()
    {
        using var world = new World();
        MakeCursor(world);
        using var driver = new PointerReplaySystem(world, Plan(0,
            new PointerCommand { Kind = PointerCommandKind.Type, Text = "ab" }));

        var snapshots = new List<Keys[]>();
        Run(driver, 4, _ => snapshots.Add(driver.ReadKeyboard().GetPressedKeys()));

        Assert.Equal(new[] { Keys.A }, snapshots[0]);
        Assert.Empty(snapshots[1]);
        Assert.Equal(new[] { Keys.B }, snapshots[2]);
        Assert.Empty(snapshots[3]);
    }

    // ── the file gate ───────────────────────────────────────────────────────────────────────

    /// <summary>No <c>pointer_replay.json</c> → no driver. This is the gate that keeps a normal run
    /// byte-identical to one from before the channel existed.</summary>
    [Fact]
    public void TryLoad_WithoutAPlanFile_BuildsNoDriver()
    {
        using var world = new World();
        var dir = Path.Combine(Path.GetTempPath(), "monodreams_pointer_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(PointerReplaySystem.TryLoad(dir, world));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A hand-written plan round-trips: camelCase kinds, the flat command shape, and the
    /// defaults a terse script relies on (left button, hold 1, tail 2).</summary>
    [Fact]
    public void TryLoad_ReadsAHandWrittenPlan_WithItsDefaults()
    {
        using var world = new World();
        var dir = Path.Combine(Path.GetTempPath(), "monodreams_pointer_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, PointerReplayPlan.FileName), """
            {
              "description": "hand written",
              "commands": [
                { "kind": "label", "text": "stage 1" },
                { "kind": "move", "x": 120, "y": 240 },
                { "kind": "click" }
              ]
            }
            """);

            var plan = PointerReplayPlan.TryLoad(dir);
            Assert.NotNull(plan);
            Assert.Equal(3, plan.Commands.Count);
            Assert.Equal(PointerCommandKind.Label, plan.Commands[0].Kind);
            Assert.Equal(120f, plan.Commands[1].X!.Value);
            Assert.Equal(PointerButton.Left, plan.Commands[2].Button);
            Assert.Equal(1, plan.Commands[2].Hold);
            Assert.Equal(2, plan.TailFrames);

            using var driver = PointerReplaySystem.TryLoad(dir, world);
            Assert.NotNull(driver);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
