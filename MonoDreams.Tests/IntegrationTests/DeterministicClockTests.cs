using System.Text.RegularExpressions;
using MonoDreams.LevelEditor.Channel;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// The double-run byte-identity precheck for the ECS migration's identity gate (issue #119,
/// contract items 49/68): the same headless demo, run twice under the deterministic-input
/// protocol below, must produce <b>byte-identical</b> PNG captures. It is runnable on any
/// branch — a C7 "candidate vs baseline" comparison only means something after this precheck
/// passes on the branch being gated, because it is what proves a pixel diff would be a
/// behaviour change and not run-to-run noise.
///
/// <para>The protocol composes knobs that all already exist — nothing here relaxes the
/// byte-identity criterion (the comparison itself takes no tolerance and skips no frames):</para>
/// <list type="bullet">
///   <item><b>Deterministic clock</b>: <c>--headless</c> injects the fixed-step clock, so dt and
///   <c>TotalGameTime</c> are frame-derived — the wallclock is never read
///   (<c>HeadlessClockTests</c> pins the time series itself).</item>
///   <item><b>No hardware mouse</b>: <c>MONODREAMS_EDITOR=1</c> plus a present
///   <c>editor_op_plan.json</c> is the switch every screen wires to
///   <c>CursorInputSystem.SkipHardwareRead</c>. Without it a headless run samples
///   <c>Mouse.GetState()</c>, whose window-relative position varies per launch (the hidden
///   window lands wherever the OS puts it), and the rendered cursor arrow lands on different
///   pixels run to run — byte-identity would then hold only while the developer's mouse
///   happens not to hover the hidden window.</item>
///   <item><b>The sim actually runs</b>: the editor flag boots in Edit (frozen), so the plan's
///   single op resumes Play at frame 0. A frozen scene would be byte-identical trivially and
///   prove nothing about the simulation's determinism.</item>
///   <item><b>Final frame only</b> (<c>captureEvery: 0</c>): the frame-0 window-backbuffer read
///   is not reliable — a partially composited first frame has been observed — while a
///   steady-state frame reads back consistently. The final frame is also the strongest single
///   observable: every pixel of frame N-1 depends on the whole run's history.</item>
/// </list>
///
/// <para>The <b>physics</b> demo is deliberately absent: <c>PhysicsDemoScreen</c> creates an
/// unseeded <c>Random</c> for ball radius/spawn/velocity, so its scene CONTENT differs per
/// process — a nondeterminism no clock or input protocol can absorb. It stays outside the
/// byte-identity precheck until its RNG is seeded (tracked for the C7 identity gate). That
/// exclusion is data, not prose: it lives in <see cref="Excluded"/> and
/// <see cref="Precheck_CoversEveryDemoScreen_OrNamesTheExclusionAndWhy"/> fails the moment the
/// covered set plus the excluded set stops being <i>every</i> screen the host can boot — so a
/// screen added later, or one quietly dropped from the run list, cannot shrink the precheck in
/// silence.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class DeterministicClockTests
{
    private const int Frames = 180;

    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    /// <summary>The screens the precheck actually runs. Read both by the theory and by the coverage
    /// guard, so the guard can never certify a list the theory does not use.</summary>
    private static readonly string[] Covered = ["launcher", "camera", "dialogue", "ui", "audio"];

    /// <summary>Screens knowingly outside the precheck, each with the reason it cannot join yet. An
    /// entry here is a debt, not a waiver: the reason is printed by the coverage guard's failure
    /// message and by any C7 gate that quotes this set.</summary>
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["physics"] = "PhysicsDemoScreen builds its scene from an unseeded Random (ball radius, spawn " +
                      "point, initial velocity), so the scene CONTENT differs per process — seed it " +
                      "before this screen can carry a pixel-identity claim.",
    };

    public static IEnumerable<object[]> CoveredScreens => Covered.Select(screen => new object[] { screen });

    /// <summary>
    /// The op plan whose PRESENCE is what flips <c>SkipHardwareRead</c> on (each screen checks
    /// <c>Overlay.HasEditorOpPlan</c>); its one op resumes Play. The huge tail keeps the op
    /// driver from requesting exit before the host's own <c>--frames</c> exit fires, so the run
    /// length stays owned by <see cref="Frames"/>.
    /// </summary>
    private static EditorOpPlan DeterministicInputPlan() => new()
    {
        Description = "byte-identity precheck: plan presence sets SkipHardwareRead; Play@0 resumes the sim",
        Ops = { new EditorOp { Frame = 0, Kind = EditorOpKind.Play } },
        TailFrames = 100_000,
    };

    [Theory]
    [MemberData(nameof(CoveredScreens))]
    public async Task Demo_RunTwiceHeadless_ProducesByteIdenticalPngs(string screen)
    {
        var first = await RunOnce(screen);
        var second = await RunOnce(screen);

        GameTestRunner.AssertScreenshotsByteIdentical(first, second, screen);
    }

    /// <summary>
    /// The precheck's worth is its SCOPE: "the demos are byte-reproducible" means nothing if the
    /// sentence quietly comes to cover three screens out of seven. This reads the host's own screen
    /// registry (<c>MonoDreams.Demos/Screens/DemoScreens.cs</c>) and requires every id in it to be
    /// either run by the theory or named in <see cref="Excluded"/> with a reason — so adding a demo
    /// screen, or dropping one from the run list, fails here instead of silently narrowing what the
    /// C7 identity gate is allowed to claim.
    ///
    /// <para>Source-scanned rather than reflected because <c>MonoDreams.Tests</c> deliberately does
    /// not reference <c>MonoDreams.Demos</c> (the host is spawned as a process), and because the
    /// committed source is what a reviewer reads — the same lint idiom as
    /// <c>EditorThemeLintTests</c>.</para>
    /// </summary>
    [Fact]
    public void Precheck_CoversEveryDemoScreen_OrNamesTheExclusionAndWhy()
    {
        var registered = RegisteredDemoScreens();

        Assert.True(
            registered.Count >= Covered.Length,
            $"parsed only {registered.Count} screen id(s) from DemoScreens.cs — the parse, not the " +
            "coverage, is what broke");

        var accounted = Covered.Concat(Excluded.Keys).ToHashSet();

        var unaccounted = registered.Where(screen => !accounted.Contains(screen)).ToList();
        Assert.True(
            unaccounted.Count == 0,
            $"demo screen(s) [{string.Join(", ", unaccounted)}] are neither run by " +
            $"{nameof(Demo_RunTwiceHeadless_ProducesByteIdenticalPngs)} nor listed in {nameof(Excluded)}. " +
            "Add them to the theory, or exclude them with the reason they cannot be byte-reproducible " +
            "yet — an unlisted screen makes the precheck claim less than it appears to.");

        var stale = accounted.Where(screen => !registered.Contains(screen)).ToList();
        Assert.True(
            stale.Count == 0,
            $"[{string.Join(", ", stale)}] is covered or excluded here but no longer registered in " +
            "DemoScreens.cs — a renamed screen leaves the theory running a nonexistent one.");
    }

    /// <summary>Short screen names (<c>demos.camera</c> → <c>camera</c>) from the host's registry — the
    /// same vocabulary <c>--screen</c> takes.</summary>
    private static HashSet<string> RegisteredDemoScreens()
    {
        var path = Path.Combine(GameTestRunner.RepoRoot(), "MonoDreams.Demos", "Screens", "DemoScreens.cs");
        Assert.True(File.Exists(path), $"demo screen registry not found at {path}");

        var ids = Regex.Matches(File.ReadAllText(path), @"const\s+string\s+\w+\s*=\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        var unprefixed = ids.Where(id => !id.StartsWith("demos.", StringComparison.Ordinal)).ToList();
        Assert.True(
            unprefixed.Count == 0,
            $"screen id(s) [{string.Join(", ", unprefixed)}] do not use the demos.* prefix this test " +
            "strips to recover the --screen name; teach it the new shape rather than skipping them.");

        return ids.Select(id => id["demos.".Length..]).ToHashSet();
    }

    private static async Task<GameTestResult> RunOnce(string screen)
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen,
            frames: Frames,
            captureEvery: 0, // final frame only — see the class doc on the frame-0 window read
            sampleEvery: 0,
            environment: EditorEnv,
            editorOpPlan: DeterministicInputPlan());

        result.AssertExitedCleanly();
        // The clock is what makes the run's TIME deterministic...
        result.AssertLogContains("Headless clock: deterministic fixed step");
        // ...the composed op driver proves the plan loaded (plan present => SkipHardwareRead:
        // the run's INPUT is deterministic)...
        result.AssertLogContains("editor.opDriver");
        // ...and Play must have resumed the sim, or this would compare a frozen scene.
        result.AssertLogContains("Transport: Playing.");
        result.AssertLogContains($"Headless run complete after {Frames} frames");
        // A blank frame pair would be byte-identical and prove nothing.
        result.AssertScreenshotNonBlank();
        return result;
    }
}
