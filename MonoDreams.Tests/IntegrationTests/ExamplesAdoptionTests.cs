using MonoDreams.Debug.Input;
using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// End-to-end protection for the reference head's adoption of the #81 feature wave (issue #115):
/// the level-selection menu now picks through the <c>ui</c> module's <c>UIFocusSystem</c> (one
/// pointer pick, one click owner), shows a <c>TooltipComponent</c> label after a dwell, and the
/// desktop head declares its presentation policy and its window decision in the log.
///
/// <para>The menu is the right screen for this: it is mouse-first (no keyboard vocabulary), so the
/// scripted-pointer channel is the only way to drive it, and it is the screen a newcomer copies.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class ExamplesAdoptionTests
{
    /// <summary>Centre of the menu's "Level 1" button in authoring coordinates: the auto-layout
    /// solver centres the button column on the 1920x1080 virtual surface, and this button sits one
    /// 50px gap above the "Runner" button that <see cref="PointerReplayTests"/> aims at (960, 610).</summary>
    private const float Level1ButtonX = 960f;
    private const float Level1ButtonY = 530f;

    /// <summary>
    /// The dwell is in SECONDS (0.35 s of game time) while a pointer plan waits in FRAMES — and a
    /// headless Examples run is uncapped (no vsync, no fixed timestep), so those 0.35 s are
    /// thousands of frames. The gate is generous on purpose; it exists to fail the run with a
    /// diagnosable ERROR rather than to bound it tightly.
    /// </summary>
    private const int TooltipTimeoutFrames = 60000;

    /// <summary>
    /// The same gates in a WINDOWED run, where the head keeps MonoGame's fixed timestep: a frame is
    /// 1/60 s, so ten seconds is ~600 frames — generous for a 0.35 s dwell and for a 1.5 s capture
    /// interval, and short enough that a broken run fails with a diagnosable <c>ERROR</c> line
    /// instead of hanging until the process timeout.
    /// </summary>
    private const int WindowedGateTimeoutFrames = 1200;

    /// <summary>The capture channel's per-file line, written only AFTER the PNG is on disk. Both a
    /// pointer gate and an assertion key off it, so it lives in one place.</summary>
    private const string ScreenshotSavedLine = "Screenshot saved:";

    /// <summary>The menu itself runs no <c>InputReplaySystem</c>, so this plan only matters once the
    /// click has moved the session onto the game screen — where it drains and exits.</summary>
    private static InputReplayPlan MenuBoot(string description) => new()
    {
        StartScreen = "LevelSelection",
        Description = description,
        Commands =
        [
            new InputReplayCommand { Action = "Exit", Type = "press", Time = 6.0f },
            new InputReplayCommand { Action = "Exit", Type = "release", Time = 6.1f },
        ],
    };

    /// <summary>
    /// The headline scenario from issue #115: the pointer moves onto a menu button, the tooltip
    /// appears by itself after the dwell, the click on that same button loads the level.
    ///
    /// <para>The tooltip's appearance is gated on the tooltip ENTITY existing — <c>TooltipSystem</c>
    /// spawns its panel as <c>EntityInfoComponent("Tooltip", "panel")</c> — which is an observable of
    /// the whole chain: <c>UIFocusSystem</c> resolved the pick from the injected cursor, the picked
    /// entity carried a <c>TooltipComponent</c>, and the dwell elapsed. Then the same pick answers the
    /// click, which is the "one click, one owner" arbitration this menu adopted.</para>
    /// </summary>
    [Fact]
    public async Task PointerDwellOnALevelButton_ShowsItsTooltip_AndTheClickLoadsTheLevel()
    {
        var result = await GameTestRunner.RunAsync(
            MenuBoot("Menu: dwell for the tooltip, then click Level 1"),
            timeoutSeconds: 120,
            pointerPlan: new PointerReplayPlan
            {
                Description = "hover Level 1 until its tooltip shows, then click it",
                TailFrames = 5,
                Commands =
                [
                    // Let the auto-layout solver place the buttons before aiming at one.
                    new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 10 },
                    new PointerCommand { Kind = PointerCommandKind.Label, Text = "aim-at-level1" },
                    new PointerCommand { Kind = PointerCommandKind.Move, X = Level1ButtonX, Y = Level1ButtonY },
                    new PointerCommand
                    {
                        Kind = PointerCommandKind.WaitUntil,
                        Entity = "Tooltip",
                        TimeoutFrames = TooltipTimeoutFrames,
                    },
                    new PointerCommand { Kind = PointerCommandKind.Label, Text = "tooltip-visible" },
                    new PointerCommand { Kind = PointerCommandKind.Click, Hold = 2 },
                    // Only reached if the click missed: the plan then drains and exits by itself.
                    new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 30 },
                ],
            });

        result.AssertExitedCleanly();
        result.AssertLogContainsInOrder(
            "[pointer] label: aim-at-level1",
            $"[pointer] move to ({Level1ButtonX:F0}, {Level1ButtonY:F0})",
            "[pointer] waitUntil entity=\"Tooltip\"",
            // The gate opened: a Tooltip entity existed, i.e. the pick + dwell + label spawn all ran.
            "[pointer] label: tooltip-visible",
            "[pointer] click Left",
            // The click travelled UIFocusSystem → UIFocusActivated → ButtonInteractionSystem →
            // ScreenTransitionRequest → the game screen's native-first level load.
            "Loaded scene 'Levels/Blender_Level.mdscene'",
            "Replay complete. Exiting game.");

        // A tooltip that never showed would have logged the gate's timeout and carried on, so the
        // ordered assertion above would still pass on the label. This is what makes it a real gate.
        Assert.DoesNotContain(result.LogLines,
            line => line.Contains("TIMED OUT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The head DECLARES its two spaces and its presentation policy, and says out loud that a
    /// headless run opts out of window fitting. Those three lines are the observable for "which
    /// resolution are these coordinates in, and how does the frame reach the window" — the questions
    /// #88/#89 answered in the engine and this issue wired into the reference head.
    /// </summary>
    [Fact]
    public async Task TheDesktopHead_LogsItsRenderSpaceAndPresentationPolicy()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "LevelSelection",
            Description = "Boot the menu headless and read the head's window/presentation decision",
            Commands =
            [
                new InputReplayCommand { Action = "Exit", Type = "press", Time = 0.5f },
                new InputReplayCommand { Action = "Exit", Type = "release", Time = 0.6f },
            ],
        },
        timeoutSeconds: 60,
        pointerPlan: new PointerReplayPlan
        {
            // The menu has no InputReplaySystem of its own, so the pointer plan owns the exit here.
            Description = "idle a few frames, then let the drained plan exit",
            TailFrames = 2,
            Commands = [new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 5 }],
        });

        result.AssertExitedCleanly();
        // Authoring space == render space in the shipped settings (LayoutWidth/Height default to 0,
        // i.e. "same as the render dimension"), which is exactly what the line has to say.
        result.AssertLogContains("Render space: authoring=1920x1080, render=1920x1080, scale=1");
        result.AssertLogContains("Presentation policy declared by the head: 'Default'");
        // Headless is the one branch that does NOT fit the window — and it says so.
        result.AssertLogContains("window sizing (WindowFit) skipped");
        Assert.DoesNotContain(result.LogLines,
            line => line.Contains("[foundation] WindowFit:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The other half of the same decision, and the only one a headless run cannot show: a WINDOWED
    /// desktop run sizes its window through <c>WindowFit</c> (issue #86) — here pinned to an exact
    /// size by <c>MONODREAMS_WINDOW</c>, which is what makes the assertion machine-independent — and
    /// the frame capture reads a NAMED render target (issue #91), so the evidence keeps the target's
    /// fixed 1920x1080 geometry even though the window is 800x600. That last inequality is the point:
    /// once the window follows the player's display, a window-sized screenshot is not comparable
    /// across machines, and a target-sized one is.
    ///
    /// <para>The target captured is <b>HUD</b>, and that choice is load-bearing rather than
    /// incidental: <c>TooltipSystem</c> draws its panel and label on a screen-space target (HUD by
    /// default) and <c>MasterRenderSystem</c> renders only the entities whose
    /// <c>DrawComponent.Target</c> equals the pass's own id, so the tooltip is in the HUD target and
    /// in NO other. A <c>Main</c> capture would picture the menu's buttons, labels and the
    /// <c>HighlightComponent</c> outline — all of which are Main-target — and could not, by
    /// construction, contain the #95 primitive this issue adopted. Capturing HUD, after the plan has
    /// gated on the tooltip existing, is what makes the file evidence OF the tooltip.</para>
    /// </summary>
    [Fact]
    public async Task WindowedRun_FitsTheWindow_AndCapturesTheTooltipLayerAtItsOwnResolution()
    {
        const int windowWidth = 800;
        const int windowHeight = 600;
        const int targetWidth = 1920;
        const int targetHeight = 1080;

        var result = await GameTestRunner.RunAsync(
            MenuBoot("Windowed menu boot: fit the window, capture the HUD target with the tooltip up"),
            timeoutSeconds: 180,
            environment: new Dictionary<string, string>
            {
                ["MONODREAMS_WINDOW"] = $"{windowWidth}x{windowHeight}",
                ["MONODREAMS_SCREENSHOT"] = "png",
                // ONE shot, taken late enough to be of the tooltip: the capture clock starts with the
                // run, and at 60 fps (a windowed run keeps MonoGame's fixed timestep) the plan's
                // 20-frame settle plus the 0.35 s dwell put the tooltip on screen by ~0.7 s. The
                // clock ticks in Draw, so it can only LAG game time, never lead it.
                ["MONODREAMS_SCREENSHOT_INTERVAL"] = "1.5",
                ["MONODREAMS_SCREENSHOT_MAX_FRAMES"] = "1",
                ["MONODREAMS_SCREENSHOT_TARGET"] = "HUD",
            },
            pointerPlan: new PointerReplayPlan
            {
                // The pointer plan owns the exit (the menu runs no InputReplaySystem).
                Description = "hover Level 1 in a real window until its tooltip is captured",
                TailFrames = 5,
                Commands =
                [
                    new PointerCommand { Kind = PointerCommandKind.WaitUntil, Frames = 20 },
                    new PointerCommand { Kind = PointerCommandKind.Move, X = Level1ButtonX, Y = Level1ButtonY },
                    new PointerCommand
                    {
                        Kind = PointerCommandKind.WaitUntil,
                        Entity = "Tooltip",
                        TimeoutFrames = WindowedGateTimeoutFrames,
                    },
                    new PointerCommand { Kind = PointerCommandKind.Label, Text = "tooltip-visible" },
                    // Wait for the FILE, not for a frame count: the capture writes its line after the
                    // PNG is on disk, so the run cannot exit mid-write and leave a truncated shot.
                    new PointerCommand
                    {
                        Kind = PointerCommandKind.WaitUntil,
                        Log = ScreenshotSavedLine,
                        TimeoutFrames = WindowedGateTimeoutFrames,
                    },
                ],
            },
            headless: false);

        var shots = Directory.GetFiles(result.DebugDir, "*.png");
        try
        {
            result.AssertExitedCleanly();

            // (a) the head asked WindowFit for the window, and the override was honoured verbatim.
            result.AssertLogContains(
                $"[foundation] WindowFit: render {targetWidth}x{targetHeight}");
            result.AssertLogContains($"window {windowWidth}x{windowHeight}, mode Override");

            // (b) the presentation policy resolved against THAT window, not against the render size.
            Assert.Contains(result.LogLines, line =>
                line.Contains("Presentation:", StringComparison.OrdinalIgnoreCase)
                && line.Contains($"window {windowWidth}x{windowHeight}", StringComparison.OrdinalIgnoreCase));

            // (c) the capture channel recorded what its files are pictures of…
            Assert.Contains(result.LogLines, line =>
                line.Contains("ScreenshotCaptureSystem initialized")
                && line.Contains("source: HUD render target"));

            // (d) …the shot was taken while the tooltip was up (the gate opened first), and the HUD
            // layer it read was not empty — a tooltip-less HUD pass clears to transparent, which is
            // exactly the blank frame this metric reports.
            result.AssertLogContainsInOrder("[pointer] label: tooltip-visible", ScreenshotSavedLine);
            Assert.Contains(result.LogLines, line =>
                line.Contains(ScreenshotSavedLine) && line.Contains("nonBlank=True"));
            Assert.DoesNotContain(result.LogLines,
                line => line.Contains("TIMED OUT", StringComparison.OrdinalIgnoreCase));

            // …and every file is the TARGET's resolution, never the window's.
            Assert.NotEmpty(shots);
            Assert.All(shots, path =>
            {
                var (width, height) = ReadPngSize(path);
                Assert.Equal(targetWidth, width);
                Assert.Equal(targetHeight, height);
            });
        }
        finally
        {
            foreach (var path in shots)
            {
                try { File.Delete(path); }
                catch (IOException) { /* best effort */ }
            }
        }
    }

    /// <summary>Width/height out of a PNG's IHDR (bytes 16..23, big-endian) — enough to assert the
    /// geometry without decoding the image (the tests have no GraphicsDevice).</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        var header = new byte[24];
        using (var stream = File.OpenRead(path))
        {
            var read = stream.Read(header, 0, header.Length);
            Assert.Equal(header.Length, read);
        }

        return (
            global::System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)),
            global::System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
    }
}
