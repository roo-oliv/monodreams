using MonoDreams.Debug.Input;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Channel;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// End-to-end protection for the scripted-pointer channel (issue #90) on a REAL, mouse-first screen:
/// the Examples level-selection menu, which has no keyboard vocabulary at all — the existing
/// <c>input_replay.json</c> can say "Jump" but can never say "move to (960, 610) and click", so
/// before this channel the menu had no scripted-verification story.
///
/// <para>The spawned run proves the whole chain: the file gate (<c>pointer_replay.json</c> in the
/// run's debug dir) → the driver → injection into the real <c>CursorInputComponent</c> →
/// <c>ButtonInteractionSystem</c> hit-testing the injected world position and acting on the injected
/// release edge → a screen transition. Nothing about the click is simulated: the same components a
/// hand on the mouse would fill are the ones the assertion depends on.</para>
///
/// <para>The click coordinates are authoring space (the menu's 1920x1080 virtual surface), which is
/// the space the auto-layout solver places the buttons in — the "Runner" button's centre.</para>
///
/// <para>In the <see cref="ContentTreeGuardCollection"/>: the file-gate case runs editor-enabled (the
/// menu owns no keyboard replay to exit with), so the real-content-tree tripwire brackets it.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class PointerReplayTests
{
    /// <summary>Centre of the level-selection menu's "Runner" button in authoring coordinates
    /// (world bounds {X:-37 Y:55 W:75 H:30} on a 1920x1080 virtual surface with the camera at the
    /// origin, i.e. authoring = world + half-extent).</summary>
    private const float RunnerButtonX = 960f;
    private const float RunnerButtonY = 610f;

    /// <summary>The menu itself runs no <c>InputReplaySystem</c>, so this plan only matters after the
    /// pointer click has moved the session onto the runner screen — where it drains and exits. It must
    /// carry at least one command, or the replay is disabled and nothing would ever exit.</summary>
    private static InputReplayPlan MenuBoot(string description) => new()
    {
        StartScreen = "LevelSelection",
        Description = description,
        Commands =
        [
            new InputReplayCommand { Action = "Exit", Type = "press", Time = 2.0f },
            new InputReplayCommand { Action = "Exit", Type = "release", Time = 2.1f },
        ],
    };

    /// <summary>
    /// The headline: a scripted pointer clicks a menu button and the game changes screens. Proof that
    /// the injected cursor travels the real picking/interaction path, not a shortcut around it.
    /// </summary>
    [Fact]
    public async Task ScriptedClick_OnAMenuButton_DrivesTheRealInteractionPipeline()
    {
        var result = await GameTestRunner.RunAsync(
            MenuBoot("Pointer replay clicks the Runner button on the level-selection menu"),
            timeoutSeconds: 60,
            pointerPlan: new PointerReplayPlan
            {
                Description = "move onto the Runner button and click it",
                TailFrames = 5,
                Commands =
                [
                    new PointerCommand { Kind = PointerCommandKind.Label, Text = "aim-at-runner" },
                    // Let the auto-layout solver place the buttons before aiming at one.
                    new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 10 },
                    new PointerCommand { Kind = PointerCommandKind.Move, X = RunnerButtonX, Y = RunnerButtonY },
                    new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 2 },
                    new PointerCommand { Kind = PointerCommandKind.Label, Text = "click-runner" },
                    new PointerCommand { Kind = PointerCommandKind.Click, Hold = 2 },
                    // Only reached if the click missed: the plan then drains and exits by itself
                    // instead of leaving the menu up until the harness times out.
                    new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 30 },
                ],
            });

        result.AssertExitedCleanly();
        result.AssertLogContainsInOrder(
            "[pointer] Plan loaded",
            "[pointer] label: aim-at-runner",
            $"[pointer] move to ({RunnerButtonX:F0}, {RunnerButtonY:F0})",
            "[pointer] label: click-runner",
            "[pointer] click Left",
            // The injected release edge reached ButtonInteractionSystem, which published the
            // transition — the whole point of injecting instead of simulating.
            "Loading InfiniteRunner screen",
            "Replay complete. Exiting game.");
    }

    /// <summary>
    /// The gate that keeps an unattended run honest: a <c>waitUntil</c> whose condition never comes
    /// true gives up after its timeout (with an ERROR naming the predicate), the rest of the script
    /// still runs, and the drained plan exits the game itself. A stuck scenario must produce a
    /// diagnosable log, never a CI hang.
    /// </summary>
    [Fact]
    public async Task WaitUntilThatNeverComesTrue_TimesOutAndTheRunStillEndsItself()
    {
        var result = await GameTestRunner.RunAsync(
            MenuBoot("Pointer replay waits for an entity that never spawns"),
            timeoutSeconds: 60,
            pointerPlan: new PointerReplayPlan
            {
                Description = "gate on an entity that never spawns",
                TailFrames = 2,
                Commands =
                [
                    new PointerCommand
                    {
                        Kind = PointerCommandKind.WaitUntil,
                        Entity = "NeverSpawnsOnTheMenu",
                        TimeoutFrames = 30,
                    },
                    new PointerCommand { Kind = PointerCommandKind.Label, Text = "past-the-gate" },
                ],
            });

        result.AssertExitedCleanly();
        result.AssertLogContainsInOrder(
            "[pointer] waitUntil entity=\"NeverSpawnsOnTheMenu\"",
            "TIMED OUT",
            "[pointer] label: past-the-gate",
            "[pointer] Plan complete");
        // The gate held: nothing was clicked, so the menu never transitioned.
        Assert.DoesNotContain(result.LogLines,
            line => line.Contains("Loading InfiniteRunner screen", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The file gate, asserted on the very screen that wires the channel. Without a
    /// <c>pointer_replay.json</c> the menu composes no driver at all — the same "absent file,
    /// byte-identical run" contract the input replay has, which is what makes the channel safe to
    /// leave wired in a shipped screen. (The menu owns no keyboard replay, so this run exits through
    /// the editor-op channel, exactly as <c>UniversalOverlayTests</c> does.)
    /// </summary>
    [Fact]
    public async Task WithoutAPointerPlan_TheMenuComposesNoDriver()
    {
        var result = await GameTestRunner.RunAsync(
            MenuBoot("No pointer plan: nothing pointer-related is constructed"),
            timeoutSeconds: 60,
            environment: new Dictionary<string, string> { ["MONODREAMS_EDITOR"] = "1" },
            editorOpPlan: new EditorOpPlan
            {
                Description = "idle a few frames, then exit",
                Ops = [new EditorOp { Frame = 5, Kind = EditorOpKind.MoveCursor, X = 100, Y = 100 }],
                TailFrames = 5,
            });

        result.AssertExitedCleanly();
        result.AssertLogContains("Editor overlay composed on LevelSelectionScreen");
        Assert.DoesNotContain(result.LogLines,
            line => line.Contains("[pointer]", StringComparison.OrdinalIgnoreCase));
    }
}
