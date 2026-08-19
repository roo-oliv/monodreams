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
///   <item><b>No hardware input</b>: <c>MONODREAMS_EDITOR=1</c> plus a present
///   <c>editor_op_plan.json</c> is the switch every screen wires to
///   <c>DemoKeyboard.Engage</c>, which pins BOTH hardware legs — the mouse
///   (<c>CursorInputSystem.SkipHardwareRead</c>) and the keyboard (the demos' shared
///   <c>DemoKeyboard.Read</c> gate, plus the <c>SkipHardwareRead</c> of every
///   <c>AKeyboardInputHandlingSystem</c> in the screen — including the ENGINE-declared
///   <c>DefaultEditorKeys</c>, which a screen passes as <c>_editor.Keys</c>). The editor overlay's own
///   six keyboard readers (both panels, the dialog, the context menu, the modal transform, the
///   shortcut chord tracker) are on the same gate, because <c>DemoEditor</c> hands the overlay
///   <c>readKeyboard: DemoKeyboard.Read</c>: they are woven <c>RunNormally</c> and are inert only
///   while no editor UI is open, so "the plan opens no panel today" is not a property the protocol
///   may rest on. Both halves are linted here rather than trusted — the overlay CONSTRUCTION must
///   carry the seam (<c>EditorKeyboardSeamLintTests</c> only proves the overlay forwards a seam it is
///   given), and the seam file's own exemption is one gated read, not a waiver for the file. Without
///   the mouse leg a headless run
///   samples <c>Mouse.GetState()</c>, whose window-relative position varies per launch (the hidden
///   window lands wherever the OS puts it), and the rendered cursor arrow lands on different
///   pixels run to run. Without the keyboard leg a key held while the hidden window happens to own
///   focus moves the camera demo's ball, advances the dialogue, or types into the UI demo's text
///   field in one run only — the window hide is best-effort and the focus-steal hint is
///   macOS-only, so "nothing is focused" is not a guarantee the protocol may rest on. Engaging
///   both is observable: the run logs <c>DemoKeyboard.EngagedLog</c> and
///   <see cref="RunOnce"/> asserts it, so losing the wiring is a red test rather than an
///   intermittent byte diff.</item>
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
/// exclusion is data, not prose: it lives in <see cref="Excluded"/>, its cause is a typed
/// <see cref="ExclusionCause"/> rather than a sentence, and
/// <see cref="Precheck_CoversEveryDemoScreen_OrNamesTheExclusionAndWhy"/> fails the moment the
/// covered set plus the excluded set stops being <i>every</i> screen the host can boot — so a
/// screen added later, or one quietly dropped from the run list, cannot shrink the precheck in
/// silence.</para>
///
/// <para>"Physics is the only nondeterministic source <b>in the sources a demo screen owns</b>" is
/// likewise checked rather than asserted in prose:
/// <see cref="Precheck_CoveredScreensPinEveryNondeterministicSource_AndTheExclusionReasonStillHolds"/>
/// scans every <c>.cs</c> file of each covered screen's demo directory PLUS the demo-owned sources
/// every screen composes — <c>MonoDreams.Demos/UI</c> (the shared widgets, palette, shape builder and
/// the demos' own button system) and the host root itself (<c>Game1</c>, <c>DemoKeyboard</c>,
/// <c>DemoEditor</c>, the headless clock). That root list is enumerated against the host's directory
/// tree rather than trusted (<see cref="Precheck_ScansEveryDirectoryOfTheDemosHost"/>), so a new
/// <c>MonoDreams.Demos/Systems/</c> cannot end up scanned by nothing. Names are resolved at the SET's
/// scope, not one file's: a <c>Random</c> declared in a shared source and target-typed-constructed from
/// a screen is still an RNG (see <see cref="CensusScope"/>). That scope is the claim's scope, and it is
/// still narrower than "the whole demo surface": the ENGINE systems a demo screen composes (cursor,
/// hierarchy, layout, camera) are not scanned here — they carry no RNG today, and the lint that would
/// cover them belongs to the engine, not to this precheck. A DORMANT source still counts as a failure — the
/// camera demo carried one (its hit-shake jitter) that only wakes when the dot enters a hit square, so
/// today's green run said nothing about tomorrow's op plan, while every record here named physics as
/// the single source and would have sent the debugger to the wrong screen. It is seeded now
/// (<c>CameraHitSystem.ShakeJitterSeed</c>), and the same test asserts the physics exclusion's typed
/// cause still holds, so seeding that screen without widening <see cref="Covered"/> fails too.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class DeterministicClockTests
{
    private const int Frames = 180;

    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    /// <summary>The line the demos host logs when the deterministic-input protocol engages
    /// (<c>MonoDreams.Demos.DemoKeyboard.EngagedLog</c> — duplicated as a literal because the test
    /// project does not reference the spawned host).</summary>
    private const string InputProtocolEngagedLog = "Deterministic input: hardware reads skipped";

    /// <summary>The screens the precheck actually runs. Read both by the theory and by the coverage
    /// guard, so the guard can never certify a list the theory does not use.</summary>
    private static readonly string[] Covered = ["launcher", "camera", "dialogue", "ui", "audio"];

    /// <summary>Why a screen is outside the precheck — a TYPED cause, not prose. The converse guard in
    /// <see cref="Precheck_CoveredScreensPinEveryNondeterministicSource_AndTheExclusionReasonStillHolds"/>
    /// switches on this, so rewording an exclusion's sentence can never quietly disable the check that
    /// the exclusion is still earned.</summary>
    private enum ExclusionCause
    {
        /// <summary>The screen's own sources build scene CONTENT from something no seed pins.</summary>
        UnpinnedNondeterminism,
    }

    /// <summary>Screens knowingly outside the precheck, each with the typed cause it cannot join yet and
    /// the reason in words. An entry here is a debt, not a waiver: the reason is printed by the coverage
    /// guard's failure message and by any C7 gate that quotes this set.</summary>
    private static readonly Dictionary<string, (ExclusionCause Cause, string Reason)> Excluded = new()
    {
        ["physics"] = (ExclusionCause.UnpinnedNondeterminism,
            "PhysicsDemoScreen builds its scene from an unseeded Random (ball radius, spawn " +
            "point, initial velocity), so the scene CONTENT differs per process — seed it " +
            "before this screen can carry a pixel-identity claim."),
    };

    /// <summary>Where each registered demo screen's OWN source lives, relative to the repo root — a
    /// DIRECTORY, scanned recursively, not a single file: a demo split across two files (systems
    /// extracted next to the screen) would otherwise leave half of itself unscanned by
    /// <see cref="Precheck_CoveredScreensPinEveryNondeterministicSource_AndTheExclusionReasonStillHolds"/>
    /// and <see cref="Precheck_EveryDemoScreenRoutesHardwareInputThroughTheProtocol"/>. Every screen ALSO
    /// gets <see cref="SharedDemoSources"/>.</summary>
    private static readonly Dictionary<string, string> ScreenSources = new()
    {
        ["launcher"] = Path.Combine("MonoDreams.Demos", "Screens"),
        ["camera"] = Path.Combine("MonoDreams", "camera", "demo"),
        ["physics"] = Path.Combine("MonoDreams", "physics", "demo"),
        ["dialogue"] = Path.Combine("MonoDreams", "dialogue", "demo"),
        ["ui"] = Path.Combine("MonoDreams", "ui", "demo"),
        ["audio"] = Path.Combine("MonoDreams", "audio", "demo"),
    };

    /// <summary>One source root of a screen's scan: a repo-relative directory and whether it is read
    /// recursively (a screen's own demo tree, and the shared demo widgets) or top-level only (the demos
    /// host root, whose subdirectories are the screens' own trees and the build output).</summary>
    private readonly record struct SourceRoot(string Relative, bool Recursive);

    /// <summary>Demo-owned sources EVERY screen composes, and which therefore belong to every screen's
    /// scan: the shared widget/palette/shape-builder layer plus the host root (<c>Game1</c>,
    /// <c>DemoKeyboard</c>, <c>DemoEditor</c>, the headless clock). Without them an unpinned
    /// <c>Random</c> in <c>ShapeBuilder</c> — composed by all five covered screens — would red every
    /// byte-identity run while the census stayed green and every record still pointed at physics.</summary>
    private static readonly SourceRoot[] SharedDemoSources =
    [
        new(Path.Combine("MonoDreams.Demos", "UI"), true),
        new("MonoDreams.Demos", false),
    ];

    /// <summary>The ONE file allowed to name <c>Keyboard.GetState()</c>: the gate itself
    /// (<c>DemoKeyboard.Read</c> IS <c>Keyboard.GetState</c> off the protocol). The exemption is
    /// per-file but NOT a blanket: <see cref="AssertTheSeamFileIsExactlyTheSeam"/> requires that file to
    /// hold exactly ONE <c>Keyboard.GetState()</c>, sitting behind the <c>SkipHardwareRead</c> gate, and
    /// no <c>Mouse.GetState()</c> at all — so a second helper (or a mouse read) added to the seam file
    /// cannot ride the exemption.</summary>
    private static readonly string KeyboardSeamSource = Path.Combine("MonoDreams.Demos", "DemoKeyboard.cs");

    /// <summary>Directories of the demos host that hold no scannable source by construction (build
    /// output). Everything else under the host root must be reachable from the census's roots —
    /// <see cref="Precheck_ScansEveryDirectoryOfTheDemosHost"/> enumerates rather than trusts, so a new
    /// <c>MonoDreams.Demos/Systems/</c> cannot be scanned by nothing while the tests stay green.</summary>
    private static readonly HashSet<string> BuildOutputDirectories =
        new(["bin", "obj"], StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<object[]> CoveredScreens => Covered.Select(screen => new object[] { screen });

    /// <summary>
    /// The op plan whose PRESENCE is what engages the deterministic-input protocol (each screen
    /// checks <c>Overlay.HasEditorOpPlan</c>); its one op resumes Play. The huge tail keeps the op
    /// driver from requesting exit before the host's own <c>--frames</c> exit fires, so the run
    /// length stays owned by <see cref="Frames"/>.
    /// </summary>
    private static EditorOpPlan DeterministicInputPlan() => new()
    {
        Description = "byte-identity precheck: plan presence pins mouse+keyboard; Play@0 resumes the sim",
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
    /// <para>The registry is only trustworthy if it is what the host actually BOOTS, so the
    /// <c>RegisterScreen</c> call sites in <c>Game1</c> are cross-checked against it: a screen
    /// registered with a raw string literal (or from a constant declared elsewhere) would be
    /// bootable while never entering the scanned registry.</para>
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

        AssertRegistryMatchesTheBootableSet(registered);

        var accounted = Covered.Concat(Excluded.Keys).ToHashSet();

        var unaccounted = registered.Where(screen => !accounted.Contains(screen)).ToList();
        Assert.True(
            unaccounted.Count == 0,
            $"demo screen(s) [{string.Join(", ", unaccounted)}] are neither run by " +
            $"{nameof(Demo_RunTwiceHeadless_ProducesByteIdenticalPngs)} nor listed in {nameof(Excluded)}. " +
            "Add them to the theory, or exclude them with the reason they cannot be byte-reproducible " +
            $"yet — an unlisted screen makes the precheck claim less than it appears to. Currently " +
            $"excluded: {DescribeExclusions()}");

        var stale = accounted.Where(screen => !registered.Contains(screen)).ToList();
        Assert.True(
            stale.Count == 0,
            $"[{string.Join(", ", stale)}] is covered or excluded here but no longer registered in " +
            "DemoScreens.cs — a renamed screen leaves the theory running a nonexistent one. Currently " +
            $"excluded: {DescribeExclusions()}");
    }

    /// <summary>
    /// The precheck's exclusion list is only worth reading if it is the WHOLE list. Every covered
    /// screen's scanned sources must therefore contain no unpinned source of nondeterminism — dormant or
    /// not: an RNG that today's pinned run never reaches (the camera demo's hit-shake jitter fires only
    /// once the dot enters a hit square) still reds a future run the moment an op plan wakes it — and then
    /// <see cref="Excluded"/>, the C8 premise and the contract all point the debugger at physics, the one
    /// screen that is not to blame. The census is not RNG-only: a wallclock or GUID read
    /// (<c>DateTime.Now</c>, <c>Guid.NewGuid()</c>, <c>Environment.TickCount</c>,
    /// <c>Stopwatch.GetTimestamp()</c>) makes scene content per-process in exactly the same way, so it
    /// fails the same test with the same message.
    ///
    /// <para>The converse is checked too, off the exclusion's TYPED cause rather than its wording: a
    /// screen excluded for unpinned nondeterminism must still have some. Seeding physics without moving
    /// it into <see cref="Covered"/> would leave the precheck five screens wide with nothing left to
    /// justify it.</para>
    /// </summary>
    [Fact]
    public void Precheck_CoveredScreensPinEveryNondeterministicSource_AndTheExclusionReasonStillHolds()
    {
        var registered = RegisteredDemoScreens();

        var unmapped = registered.Where(screen => !ScreenSources.ContainsKey(screen)).ToList();
        Assert.True(
            unmapped.Count == 0,
            $"demo screen(s) [{string.Join(", ", unmapped)}] have no entry in {nameof(ScreenSources)}, so " +
            "nothing scans them for unpinned nondeterminism. Map them to their demo source directory.");

        var staleSources = ScreenSources.Keys.Where(screen => !registered.Contains(screen)).ToList();
        Assert.True(
            staleSources.Count == 0,
            $"[{string.Join(", ", staleSources)}] is mapped in {nameof(ScreenSources)} but no longer " +
            "registered in DemoScreens.cs — the scan is reading a screen the host cannot boot.");

        foreach (var screen in Covered)
        {
            var sources = ReadScreenSources(screen);
            var scope = CensusScope.Of(sources.Select(file => file.Source));

            foreach (var (path, source) in sources)
            {
                var offenders = NondeterministicSources(source, scope);
                if (offenders.Count == 0) continue;

                var (line, snippet, why) = offenders[0];
                Assert.Fail(
                    $"'{screen}' is run by {nameof(Demo_RunTwiceHeadless_ProducesByteIdenticalPngs)} but " +
                    $"{path}:{line} is nondeterministic ('{snippet}' — {why}). Even while nothing consumes " +
                    "it, it is a byte-identity failure waiting for the input that reaches it, and every " +
                    "record here names physics as the only unpinned source in a demo screen's sources. Pin " +
                    "it with a COMPILE-TIME CONSTANT (CameraHitSystem's ShakeJitterSeed is the pattern — a " +
                    "wallclock or entropy seed is no better than none), or move the screen into " +
                    $"{nameof(Excluded)} with the reason it cannot be pinned.");
            }
        }

        foreach (var (screen, exclusion) in Excluded)
        {
            if (exclusion.Cause != ExclusionCause.UnpinnedNondeterminism) continue;

            // The screen's OWN sources only. The shared demo roots belong to every screen's scan, so
            // reading them here would let nondeterminism in ShapeBuilder (which the covered loop above
            // already fails on) stand in for the excluded screen's own — the exclusion has to be earned
            // by the screen it names.
            var sources = ReadOwnScreenSources(screen);
            var scope = CensusScope.Of(sources.Select(file => file.Source));
            Assert.True(
                sources.Any(file => NondeterministicSources(file.Source, scope).Count > 0),
                $"'{screen}' is excluded from the precheck as {nameof(ExclusionCause.UnpinnedNondeterminism)} " +
                $"({exclusion.Reason}), but no source under {ScreenSources[screen]} has any left. Move it " +
                $"into {nameof(Covered)} (and re-run the precheck) rather than leaving the byte-identity " +
                "claim narrower than the code now allows.");
        }
    }

    /// <summary>
    /// The census's roots are a hand-written list, so "every demo-owned source is scanned" holds only
    /// while that list keeps up with the host's directory tree. This enumerates it instead: every
    /// top-level directory of <c>MonoDreams.Demos</c> that holds C# source must lie inside a root the
    /// census actually reads for a COVERED screen (a <see cref="ScreenSources"/> entry of a covered
    /// screen, or a <see cref="SharedDemoSources"/> root) — build output aside.
    ///
    /// <para>Without it, coverage of <c>MonoDreams.Demos/Screens</c> rests on the launcher staying in
    /// <see cref="Covered"/>, and a new <c>MonoDreams.Demos/Systems/</c> (or a <c>ShapeBuilder</c> moved
    /// out of <c>UI/</c>) would be scanned by nothing at all while every test stayed green and the C8
    /// premise kept claiming the demo-owned sources are covered.</para>
    /// </summary>
    [Fact]
    public void Precheck_ScansEveryDirectoryOfTheDemosHost()
    {
        var repoRoot = GameTestRunner.RepoRoot();
        var hostRoot = Path.Combine(repoRoot, "MonoDreams.Demos");
        Assert.True(Directory.Exists(hostRoot), $"demos host root not found at {hostRoot}");

        var scanned = Covered.Select(screen => ScreenSources[screen])
            .Concat(SharedDemoSources.Select(root => root.Relative))
            .Select(relative => Path.GetFullPath(Path.Combine(repoRoot, relative)))
            .ToHashSet(StringComparer.Ordinal);

        var unscanned = Directory.GetDirectories(hostRoot)
            .Where(directory => !BuildOutputDirectories.Contains(Path.GetFileName(directory)))
            .Where(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Length > 0)
            .Where(directory => !scanned.Contains(Path.GetFullPath(directory)))
            .Select(directory => Path.GetRelativePath(repoRoot, directory))
            .ToList();

        Assert.True(
            unscanned.Count == 0,
            $"directory(ies) [{string.Join(", ", unscanned)}] of the demos host hold C# source that no " +
            $"covered screen's scan reads. Both {nameof(Precheck_CoveredScreensPinEveryNondeterministicSource_AndTheExclusionReasonStillHolds)} " +
            $"and {nameof(Precheck_EveryDemoScreenRoutesHardwareInputThroughTheProtocol)} would skip them, " +
            $"so an unpinned Random or a direct Keyboard.GetState() there reds a byte-identity run while " +
            $"both lints stay green. Add the directory to {nameof(SharedDemoSources)} (it belongs to every " +
            $"screen's scan) or map it in {nameof(ScreenSources)}.");
    }

    /// <summary>
    /// The protocol's INPUT leg, linted rather than trusted. Every demo screen must read the keyboard
    /// through the demos' shared gate (<c>DemoKeyboard.Read</c>) and the mouse through
    /// <c>CursorInputSystem</c>, and every screen that builds a cursor pipeline must engage the
    /// protocol (<c>DemoKeyboard.Engage</c>) — that call is what flips both legs and what emits the
    /// line <see cref="RunOnce"/> asserts.
    ///
    /// <para>Forbidding one token is not enough, because the hardware default lives in ENGINE code: a
    /// <c>TextInputSystem</c> built without <c>KeyboardStateProvider</c>, a <c>KeyChordTracker</c>
    /// built without its seam argument, or an <c>EditorOverlay</c> built without <c>readKeyboard</c>
    /// (six readers at once) reads <c>Keyboard.GetState</c> while the demo source stays clean. All
    /// three defaulting seams are linted here, on the argument's VALUE rather than its presence — a
    /// second argument that is <c>null</c>, or a <c>getKeyboardState:</c> label pointing back at
    /// <c>Keyboard.GetState</c>, defaults exactly as an omission does. So is the other blind spot: an
    /// <c>AKeyboardInputHandlingSystem</c> subclass a screen constructs but never hands to
    /// <c>Engage</c> — <c>Engage</c> logs its line either way, so <see cref="RunOnce"/>'s assertion
    /// cannot see the missing one. The subclass set is seeded from the ENGINE's declarations too, not
    /// only from what a demo declares: the demos' editor key surface (<c>DefaultEditorKeys</c>) is
    /// declared in the engine and constructed by <c>DemoEditor</c>, so a demo-only set would have left
    /// the leg the premise names entirely unchecked.</para>
    ///
    /// <para>Without this lint the keyboard leg degrades exactly the way the mouse leg used to: a
    /// screen (or a new system inside one) calls <c>Keyboard.GetState()</c> directly, every run stays
    /// green on a machine with no key held, and the byte-identity claim quietly becomes conditional on
    /// the developer's hands. Line comments are stripped first, so prose naming the forbidden call is
    /// never a false positive (unlike the nondeterminism census above, which scans comments on purpose —
    /// a commented-out RNG is a trap the next author copies).</para>
    /// </summary>
    [Fact]
    public void Precheck_EveryDemoScreenRoutesHardwareInputThroughTheProtocol()
    {
        var seamSeen = false;

        foreach (var screen in RegisteredDemoScreens())
        {
            var sources = ReadScreenSources(screen);
            var engages = false;
            var buildsCursorPipeline = false;
            var engageArguments = new List<string>();
            // Seeded with the ENGINE's own subclasses, not only the ones a demo declares: the demos
            // construct DefaultEditorKeys through DemoEditor and hand it to Engage as `_editor.Keys`,
            // and a set built from demo declarations alone would never check that leg — dropping
            // `_editor.Keys` from a screen's Engage call would keep this lint green while the editor's
            // whole key surface went back to the hardware.
            var keyboardSystems = new HashSet<string>(EngineKeyboardSystems.Value, StringComparer.Ordinal);
            var code = new List<(string Path, string Code)>();

            foreach (var (path, source) in sources)
            {
                var stripped = StripLineComments(source);
                code.Add((path, stripped));

                if (path == KeyboardSeamSource)
                {
                    // The seam itself: the ONE hardware read the protocol is built around.
                    seamSeen = true;
                    AssertTheSeamFileIsExactlyTheSeam(path, stripped);
                }
                else
                {
                    var rawRead = Regex.Match(stripped, @"\b(?:Keyboard|Mouse)\.GetState\s*\(");
                    Assert.False(
                        rawRead.Success,
                        $"'{screen}' reads the hardware directly at {path}:{LineOf(stripped, rawRead.Index)} " +
                        $"('{rawRead.Value}'). The deterministic-input protocol pins input at ONE seam: read " +
                        "the keyboard through DemoKeyboard.Read() (and the mouse through CursorInputSystem, " +
                        "whose SkipHardwareRead the protocol sets). A direct read is invisible until a key " +
                        "held during one of two byte-identity runs moves the scene in that run only.");
                }

                foreach (Match match in DefaultingTextInput.Matches(stripped))
                    Assert.True(
                        SeamValue.IsMatch(match.Value),
                        $"'{screen}' builds a TextInputSystem at {path}:{LineOf(stripped, match.Index)} " +
                        "without setting KeyboardStateProvider to the demos' gate. That property " +
                        "defaults to Keyboard.GetState INSIDE the engine, so the demo source stays clean " +
                        "while the run types the developer's keystrokes into the field. Set " +
                        "{ KeyboardStateProvider = DemoKeyboard.Read }.");

                foreach (Match match in DefaultingChordTracker.Matches(stripped))
                {
                    var arguments = ArgumentsAt(stripped, match.Index + match.Length - 1);
                    // The VALUE, not merely a second argument: `new KeyChordTracker(false, null)` has a
                    // comma and still falls back to Keyboard.GetState inside the engine.
                    Assert.True(
                        SeamValue.IsMatch(arguments),
                        $"'{screen}' builds a KeyChordTracker at {path}:{LineOf(stripped, match.Index)} " +
                        "without the demos' keyboard gate as its seam. The seam argument is optional and " +
                        "defaults to Keyboard.GetState inside the engine (an explicit `null` defaults the " +
                        "same way), so the chord table would read the hardware under the protocol. Pass " +
                        "DemoKeyboard.Read.");
                }

                foreach (Match match in OverlayConstruction.Matches(stripped))
                {
                    var arguments = ArgumentsAt(stripped, match.Index + match.Length - 1);
                    Assert.True(
                        OverlaySeamArgument.IsMatch(arguments),
                        $"'{screen}' builds an EditorOverlay at {path}:{LineOf(stripped, match.Index)} " +
                        "without 'readKeyboard: DemoKeyboard.Read'. The overlay's six keyboard readers " +
                        "(both panels, the Save dialog, the context menu, the modal transform, the " +
                        "shortcut chord tracker) each default to Keyboard.GetState INSIDE the engine, and " +
                        "the protocol REQUIRES the editor flag — so they are woven RunNormally in every " +
                        "precheck run and an overlay built without the seam hands the whole editor key " +
                        "surface back to the hardware while every other leg stays pinned. " +
                        "EditorKeyboardSeamLintTests only proves the overlay forwards the seam it is " +
                        "given; this is what proves it is given one.");
                }

                foreach (Match match in KeyboardSystemDeclaration.Matches(stripped))
                    keyboardSystems.Add(match.Groups[1].Value);

                if (stripped.Contains("new CursorInputSystem(", StringComparison.Ordinal)) buildsCursorPipeline = true;
                foreach (Match match in EngageCall.Matches(stripped))
                {
                    engages = true;
                    engageArguments.Add(ArgumentsAt(stripped, match.Index + match.Length - 1));
                }
            }

            Assert.True(
                !buildsCursorPipeline || engages,
                $"'{screen}' builds a CursorInputSystem but no source under {ScreenSources[screen]} calls " +
                "DemoKeyboard.Engage(...). Engaging the protocol is what pins BOTH hardware legs and what " +
                $"logs '{InputProtocolEngagedLog}' — the line {nameof(RunOnce)} asserts. A screen that " +
                "pins only the cursor passes every run until a keypress lands in one of them.");

            if (engages) AssertEveryKeyboardSystemIsEngaged(screen, keyboardSystems, code, engageArguments);
        }

        Assert.True(
            seamSeen,
            $"the keyboard seam {KeyboardSeamSource} was not among the scanned sources — the lint's " +
            "one exemption is pointing at a file that no longer exists, which means the raw-read rule is " +
            "no longer anchored to a seam. Update " + nameof(KeyboardSeamSource) + ".");
    }

    /// <summary>The seam file's exemption covers ONE read, not the file. It must hold exactly one
    /// <c>Keyboard.GetState()</c>, that read must sit on the <c>SkipHardwareRead</c> gate line, and it
    /// must name no <c>Mouse.GetState()</c> at all — otherwise a second helper (or a mouse read) added
    /// beside the seam would inherit a blanket waiver from the raw-read rule and sample the hardware
    /// under the protocol.</summary>
    private static void AssertTheSeamFileIsExactlyTheSeam(string path, string stripped)
    {
        var reads = Regex.Matches(stripped, @"\bKeyboard\.GetState\s*\(");
        Assert.True(
            reads.Count == 1,
            $"{path} is the protocol's ONE exempted hardware read, so it must contain exactly one " +
            $"Keyboard.GetState() — found {reads.Count}. A second reader in the seam file is exempt from " +
            "the raw-read lint by path while nothing gates it, which is the blanket the per-file " +
            "exemption exists not to be.");

        var mouse = Regex.Match(stripped, @"\bMouse\.GetState\s*\(");
        Assert.False(
            mouse.Success,
            $"{path} reads the hardware MOUSE at line {LineOf(stripped, mouse.Index)}. The seam file's " +
            "exemption covers the keyboard gate only — the mouse leg is CursorInputSystem's " +
            "SkipHardwareRead, and a mouse read here is invisible to every other check.");

        var gate = RawLine(stripped, reads[0].Index);
        Assert.True(
            Regex.IsMatch(gate, @"\bSkipHardwareRead\b[^;]*\?"),
            $"{path}'s Keyboard.GetState() is not on the SkipHardwareRead gate line ('{gate.Trim()}'). " +
            "The seam reads the hardware only OFF the protocol; an ungated read in the seam file returns " +
            "the whole demos keyboard to the hardware while every lint stays green.");
    }

    /// <summary>Every <c>AKeyboardInputHandlingSystem</c> subclass a screen declares AND constructs must
    /// reach <c>DemoKeyboard.Engage</c>'s argument list — that call is the only thing that sets its
    /// <c>SkipHardwareRead</c>, and <c>Engage</c> logs the protocol line whether or not the system was
    /// handed to it, so the run-time assertion cannot catch the omission.</summary>
    private static void AssertEveryKeyboardSystemIsEngaged(
        string screen, IReadOnlyCollection<string> keyboardSystems,
        IReadOnlyList<(string Path, string Code)> sources, IReadOnlyList<string> engageArguments)
    {
        var engaged = string.Join(" ; ", engageArguments);

        foreach (var type in keyboardSystems)
        {
            var construction = new Regex($@"\bnew\s+{Regex.Escape(type)}\s*\(", RegexOptions.None);
            var assignment = new Regex($@"(\w+)\s*=\s*new\s+{Regex.Escape(type)}\s*\(", RegexOptions.None);
            // Also every name DECLARED with the type — a system built behind a helper reaches Engage
            // under the helper's own member name (DemoEditor builds DefaultEditorKeys as `keys` and
            // exposes it as `Keys`, which is what a screen passes: `_editor.Keys`).
            var declaration = new Regex($@"\b{Regex.Escape(type)}\??\s+(\w+)\s*[;=,){{]", RegexOptions.None);

            var names = new List<string>();
            var constructed = false;
            foreach (var (_, code) in sources)
            {
                if (construction.IsMatch(code)) constructed = true;
                names.AddRange(assignment.Matches(code).Select(match => match.Groups[1].Value));
                names.AddRange(declaration.Matches(code).Select(match => match.Groups[1].Value));
            }

            if (!constructed) continue;

            var reached = engaged.Contains($"new {type}(", StringComparison.Ordinal)
                          || names.Any(name => Regex.IsMatch(engaged, $@"\b{Regex.Escape(name)}\b"));
            Assert.True(
                reached,
                $"'{screen}' constructs {type} (an AKeyboardInputHandlingSystem) but never hands it to " +
                "DemoKeyboard.Engage(...). Engage is what sets its SkipHardwareRead, and it logs " +
                $"'{InputProtocolEngagedLog}' whether or not this system was among its arguments — so the " +
                "run's own assertion stays green while this reader keeps sampling the hardware. Pass it " +
                "to Engage (or drop it from the pipeline).");
        }
    }

    /// <summary>
    /// The census's own contract, on synthetic sources — a scan whose escapes are unknown is a scan
    /// whose green run means nothing, and every form below is one a shape-matching regex accepted while
    /// the records claimed enforcement. Pinned cases prove the converse: the scan must not fail a demo
    /// that pinned its RNG properly, or the next author routes around it.
    /// </summary>
    [Theory]
    // Unpinned — every one of these must be caught.
    [InlineData("var rng = new Random();", true)]
    [InlineData("var rng = new System.Random();", true)]
    [InlineData("var rng = Random.Shared;", true)]
    [InlineData("var rng = new Random(Environment.TickCount);", true)]
    [InlineData("var rng = new Random((int)DateTime.Now.Ticks);", true)]
    [InlineData("var rng = new Random(Guid.NewGuid().GetHashCode());", true)]
    [InlineData("private Random? _rng = new();", true)]
    [InlineData("private readonly Random _rng;\n    Ctor() { _rng = new(); }", true)]
    [InlineData("private readonly Random _rng;\n    Ctor() { _rng = new(Environment.TickCount); }", true)]
    // …including the target-typed shapes a `name = new(` matcher never sees.
    [InlineData("public Random Rng { get; } = new();", true)]
    [InlineData("private Random? _rng;\n    void Ensure() { _rng ??= new(); }", true)]
    [InlineData("private Random Make() => new();", true)]
    [InlineData("private readonly Random[] _rngs = [new()];", true)]
    // …including the MULTI-LINE forms of those shapes, where the type sits more than one line above the
    // `new(` and a one-line look-back sees only whitespace and an opening bracket.
    [InlineData("private readonly Random[] _rngs =\n    [\n        new(),\n    ];", true)]
    [InlineData("private readonly Random _rng\n        =\n            new();", true)]
    // …a seed qualified by a type this source never declares is not a constant this source can pin.
    [InlineData("private const int Seed = 7;\n    private readonly Random _rng = new Random(Other.Seed);", true)]
    // …and entropy that is not an RNG at all makes scene content per-process just the same.
    [InlineData("var jitter = DateTime.Now.Millisecond * 0.01f;", true)]
    [InlineData("var id = Guid.NewGuid();", true)]
    [InlineData("var t0 = Stopwatch.GetTimestamp();", true)]
    [InlineData("var sw = new Stopwatch();", true)]
    [InlineData("var elapsed = _sw.Elapsed.TotalMilliseconds;", true)]
    [InlineData("var pid = Environment.ProcessId;", true)]
    [InlineData("var now = TimeProvider.System.GetUtcNow();", true)]
    [InlineData("var order = name.GetHashCode();", true)]
    [InlineData("var rng = new Random(name.GetHashCode());", true)]
    // Pinned — none of these may fail the scan.
    [InlineData("var rng = new Random(7);", false)]
    [InlineData("var rng = new Random(-1);", false)]
    [InlineData("var rng = new Random(seed: 7);", false)]
    [InlineData("var rng = new Random(unchecked((int)0x5EED));", false)]
    [InlineData("private const int Seed = 7;\n    private readonly Random _rng = new(Seed);", false)]
    [InlineData("private const int Seed = 7;\n    private readonly Random _rng = new(Seed + 1);", false)]
    [InlineData("private static readonly int Seed = 7;\n    private readonly Random _rng = new(Seed);", false)]
    [InlineData("class Seeds { public const int Value = 7; }\n    Random _rng = new Random(Seeds.Value);", false)]
    [InlineData("public static readonly Color Fill = new(1, 2, 3);", false)]
    [InlineData("private Vector2 RandomVelocity() => Vector2.Zero;", false)]
    // …the entropy list is a list of READS, not of names: reading an env var is not entropy.
    [InlineData("var dir = Environment.GetEnvironmentVariable(\"MONODREAMS_DEBUG_DIR\");", false)]
    public void NondeterminismCensus_MatchesOnTheType_NotOnOneSyntacticShape(string source, bool expectUnpinned)
    {
        var findings = NondeterministicSources(source);

        Assert.True(
            findings.Count > 0 == expectUnpinned,
            expectUnpinned
                ? $"the census missed an unpinned source in: {source}"
                : $"the census flagged a properly pinned source in: {source} " +
                  $"({string.Join("; ", findings.Select(f => $"{f.Snippet} — {f.Why}"))})");
    }

    /// <summary>
    /// The census's contract ACROSS files, which is the scope it actually runs in: every covered screen's
    /// scan spans its own demo tree plus two shared roots. A per-file name set and a set-wide constant
    /// pool each break in one direction — the first misses an RNG declared in one file and constructed in
    /// another, the second accepts an unpinned local seed because an unrelated file happens to declare a
    /// same-named constant. Both directions are pinned here.
    /// </summary>
    [Theory]
    // A Random-typed member declared in a shared source and target-typed-constructed from a screen is
    // still an RNG construction — the shape that made the census green while content differed per run.
    [InlineData("public static class ShapeBuilder { public static Random Jitter { get; set; } }",
        "ShapeBuilder.Jitter = new();", true)]
    // A BARE seed name resolves against the file that uses it, never against a sibling's constant: a
    // local Seed the compiler computes at runtime is not pinned by someone else's `const int Seed`.
    [InlineData("internal static class Seeds { public const int Seed = 7; }",
        "private static readonly int Seed = ComputeSeed();\n    private readonly Random _rng = new Random(Seed);", true)]
    // A qualified seed binds to the type that DECLARES it, not to every type in the declaring file.
    [InlineData("class A { public const int Seed = 7; }\n    class B { }",
        "private readonly Random _rng = new Random(B.Seed);", true)]
    // …and the cross-file forms that ARE pinned must not be flagged, or the next author routes around it.
    [InlineData("class Seeds { public const int Value = 7; }",
        "private readonly Random _rng = new Random(Seeds.Value);", false)]
    [InlineData("public static class ShapeBuilder { public static Random Jitter { get; set; } }",
        "public static readonly Color Fill = new(1, 2, 3);", false)]
    public void NondeterminismCensus_ResolvesNamesAcrossTheScannedSet_WithoutPoolingBareOnes(
        string sibling, string source, bool expectUnpinned)
    {
        var scope = CensusScope.Of([sibling, source]);

        var findings = NondeterministicSources(source, scope);

        Assert.True(
            findings.Count > 0 == expectUnpinned,
            expectUnpinned
                ? $"the census missed an unpinned source in: {source} (sibling source: {sibling})"
                : $"the census flagged a properly pinned source in: {source} " +
                  $"({string.Join("; ", findings.Select(f => $"{f.Snippet} — {f.Why}"))})");
    }

    /// <summary>Reads every <c>.cs</c> file of a registered screen's own demo directory plus the
    /// demo-owned sources every screen composes (<see cref="SharedDemoSources"/>), failing with the
    /// mapping to fix when a root moved.</summary>
    private static IReadOnlyList<(string RelativePath, string Source)> ReadScreenSources(string screen)
    {
        var roots = new List<SourceRoot> { new(ScreenSources[screen], true) };
        roots.AddRange(SharedDemoSources);
        return ReadSources(screen, roots);
    }

    /// <summary>The screen's OWN demo tree, without the shared roots — what an exclusion has to be
    /// earned by, since the shared roots belong to every screen alike.</summary>
    private static IReadOnlyList<(string RelativePath, string Source)> ReadOwnScreenSources(string screen) =>
        ReadSources(screen, [new SourceRoot(ScreenSources[screen], true)]);

    private static IReadOnlyList<(string RelativePath, string Source)> ReadSources(
        string screen, IReadOnlyList<SourceRoot> roots)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var full = Path.Combine(GameTestRunner.RepoRoot(), root.Relative);
            Assert.True(
                Directory.Exists(full),
                $"sources for screen '{screen}' not found at {root.Relative} — update " +
                $"{nameof(ScreenSources)}/{nameof(SharedDemoSources)}.");

            var found = Directory.GetFiles(full, "*.cs",
                root.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Assert.True(
                found.Length > 0,
                $"no .cs file under {root.Relative} (scanned for '{screen}') — a mapped root that reads " +
                "nothing is a scan that certifies nothing.");

            foreach (var path in found)
                files[Path.GetRelativePath(GameTestRunner.RepoRoot(), path)] = File.ReadAllText(path);
        }

        return files.Select(entry => (entry.Key, entry.Value)).ToList();
    }

    // ─── the nondeterminism census ───────────────────────────────────────────────────────────────

    /// <summary>What the census knows beyond the ONE file it is scanning. Two things cross file
    /// boundaries and each does so on its own terms:
    ///
    /// <list type="bullet">
    ///   <item><b>Random-typed names</b> span the whole scanned set. A screen's scan spans three roots,
    ///   so <c>ShapeBuilder.Jitter</c> can be declared in <c>MonoDreams.Demos/UI</c> and target-typed
    ///   constructed (<c>ShapeBuilder.Jitter = new();</c>) from the screen's own file — a per-file name
    ///   set sees an RNG in neither.</item>
    ///   <item><b>Constants</b> resolve QUALIFIED across the set and BARE only within the file that uses
    ///   them, which is how C# itself resolves them. Pooling bare names would let
    ///   <c>new Random(Seed)</c> pass because some other file declares a <c>const int Seed</c> while the
    ///   local <c>Seed</c> is read off the wallclock. A qualified name is bound to the type that
    ///   actually DECLARES it (nearest enclosing type declaration), so <c>B.Seed</c> does not resolve
    ///   against sibling class <c>A</c>'s constant, and a qualifier this scan never saw declared is not
    ///   resolvable at all.</item>
    /// </list>
    /// </summary>
    private sealed class CensusScope
    {
        private CensusScope(HashSet<string> qualifiedConstants, HashSet<string> randomNames)
        {
            Qualified = qualifiedConstants;
            RandomNames = randomNames;
        }

        /// <summary>Constants qualified by the type that declares them (<c>Seeds.Value</c>).</summary>
        private HashSet<string> Qualified { get; }

        /// <summary>Every name declared <c>Random</c>-typed anywhere in the scanned set.</summary>
        public IReadOnlySet<string> RandomNames { get; }

        public static CensusScope Of(IEnumerable<string> sources)
        {
            var qualified = new HashSet<string>(StringComparer.Ordinal);
            var randomNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var source in sources)
            {
                foreach (var (name, declaringType) in DeclaredConstants(source))
                    if (declaringType != null)
                        qualified.Add($"{declaringType}.{name}");

                foreach (Match match in RandomDeclaration.Matches(source))
                    randomNames.Add(match.Groups[1].Value);
            }

            return new CensusScope(qualified, randomNames);
        }

        /// <summary>Whether <paramref name="token"/> is a constant the next run reproduces, given the
        /// bare constants declared by the file being scanned.</summary>
        public bool Resolves(string token, IReadOnlySet<string> localConstants) =>
            token.Contains('.') ? Qualified.Contains(token) : localConstants.Contains(token);
    }

    /// <summary>Integer constants a source declares, each paired with the type declaring it (the nearest
    /// preceding type declaration, or null at file scope) — the only names the census accepts as a
    /// seed, so it distinguishes <c>new Random(ShakeJitterSeed)</c> from
    /// <c>new Random(Environment.TickCount)</c> instead of accepting "it has an argument".</summary>
    private static IReadOnlyList<(string Name, string? DeclaringType)> DeclaredConstants(string source)
    {
        var types = TypeDeclaration.Matches(source)
            .Select(match => (match.Index, Name: match.Groups[1].Value))
            .OrderBy(entry => entry.Index)
            .ToList();

        string? EnclosingType(int index)
        {
            string? nearest = null;
            foreach (var (start, name) in types)
            {
                if (start > index) break;
                nearest = name;
            }

            return nearest;
        }

        return IntegerConstant.Matches(source).Concat(LiteralStaticReadonly.Matches(source))
            .Select(match => (match.Groups[1].Value, EnclosingType(match.Index)))
            .ToList();
    }

    private static IReadOnlySet<string> LocalConstants(string source) =>
        DeclaredConstants(source).Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);

    private static readonly Regex IntegerConstant = new(
        @"\bconst\s+(?:int|uint|long|ulong|short|ushort|byte|sbyte)\s+(\w+)\s*=", RegexOptions.Compiled);

    /// <summary>A <c>static readonly</c> integer initialised from a literal — as reproducible as a
    /// <c>const</c>, and the shape a demo author reaches for when the seed is not compile-time
    /// assignable. Only literal initialisers count: <c>static readonly int Seed = Environment.TickCount</c>
    /// is the very thing the census exists to catch.</summary>
    private static readonly Regex LiteralStaticReadonly = new(
        @"\bstatic\s+readonly\s+(?:int|uint|long|ulong|short|ushort|byte|sbyte)\s+(\w+)\s*=\s*" +
        @"-?\s*(?:\d[\d_]*|0[xX][0-9a-fA-F_]+)[uUlL]{0,2}\s*;", RegexOptions.Compiled);

    private static readonly Regex TypeDeclaration = new(
        @"\b(?:class|struct|record|interface|enum)\s+(\w+)", RegexOptions.Compiled);

    /// <summary>Declarations of a <c>Random</c>-typed field/local/property/array (nullable included), so a
    /// target-typed <c>new()</c> assigned to one — inline, in a constructor body, in a property
    /// initialiser, through <c>??=</c> or inside a collection expression — is recognised as an RNG
    /// construction.</summary>
    private static readonly Regex RandomDeclaration = new(
        @"\b(?:System\.)?Random(?:\s*\[\s*\])?\??\s+(\w+)\s*[;=,){]", RegexOptions.Compiled);

    /// <summary>The same shape without the captured name — what makes a bare <c>new(…)</c> on a line a
    /// <c>Random</c> construction even when no name of ours is on it (<c>Random Make() =&gt; new();</c>,
    /// <c>Random[] _r = [new()];</c>).</summary>
    private static readonly Regex RandomTypeMention = new(
        @"\b(?:System\.)?Random(?:\s*\[\s*\])?\??\s+\w+", RegexOptions.Compiled);

    private static readonly Regex RandomConstruction = new(
        @"\bnew\s+(?:System\.)?Random\s*\(", RegexOptions.Compiled);

    /// <summary>Any target-typed construction. Which TYPE it constructs is decided by
    /// <see cref="TargetsRandom"/> from the surrounding text, rather than by requiring one syntactic
    /// shape (<c>name = new(</c>) that <c>??=</c>, <c>=&gt;</c>, a property initialiser and a collection
    /// expression all escape.</summary>
    private static readonly Regex TargetTypedConstruction = new(@"\bnew\s*\(", RegexOptions.Compiled);

    private static readonly Regex SharedRandom = new(@"\b(?:System\.)?Random\.Shared\b", RegexOptions.Compiled);

    /// <summary>Nondeterministic sources that are not RNGs at all. A demo that sizes a shape from
    /// <c>DateTime.Now.Millisecond</c> differs per process exactly as an unseeded <c>Random</c> does, and
    /// would red the byte-identity theory while every record named physics. The list covers the shapes a
    /// demo author actually reaches for: the wallclock (static and via an instance <c>Stopwatch</c> —
    /// the shape <c>GatedSystem</c> uses, and therefore the one a demo copies), per-process identity
    /// (<c>Guid.NewGuid</c>, <c>Environment.ProcessId</c>, the managed thread id) and the
    /// per-process-randomised string hash that an ordering key or a seed reaches for.</summary>
    private static readonly Regex EntropyRead = new(
        @"\b(?:DateTime|DateTimeOffset)\s*\.\s*(?:Now|UtcNow|Today)\b" +
        @"|\bGuid\s*\.\s*NewGuid\s*\(" +
        @"|\bEnvironment\s*\.\s*(?:TickCount64?|ProcessId|CurrentManagedThreadId)\b" +
        @"|\bStopwatch\s*\.\s*(?:GetTimestamp|StartNew)\s*\(" +
        @"|\bnew\s+Stopwatch\s*\(" +
        @"|\.\s*Elapsed(?:Milliseconds|Ticks)?\b" +
        @"|\bTimeProvider\b" +
        @"|\.\s*GetHashCode\s*\(", RegexOptions.Compiled);

    private static readonly Regex Identifier = new(@"\w+", RegexOptions.Compiled);

    private static readonly Regex NamedArgumentLabel = new(@"^\s*\w+\s*:(?!:)", RegexOptions.Compiled);

    private static readonly Regex IntegerLiteral = new(
        @"^-?(?:\d[\d_]*|0[xX][0-9a-fA-F_]+)[uUlL]{0,2}$", RegexOptions.Compiled);

    /// <summary>Tokens a constant seed expression may contain besides literals and constants: the casts
    /// and overflow keywords a hand-written magic seed carries (<c>unchecked((int)0x5EED)</c>).</summary>
    private static readonly string[] SeedKeywords =
        ["int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "unchecked", "checked"];

    private static readonly char[] SeedOperators = "+-*/%()&|^~<> \t\r\n".ToCharArray();

    private static readonly Regex DefaultingTextInput = new(
        @"\bnew\s+TextInputSystem\s*\([^;]*", RegexOptions.Compiled);

    private static readonly Regex DefaultingChordTracker = new(
        @"\bnew\s+KeyChordTracker\s*\(", RegexOptions.Compiled);

    private static readonly Regex OverlayConstruction = new(
        @"\bnew\s+EditorOverlay\s*\(", RegexOptions.Compiled);

    /// <summary>The overlay's seam argument, matched on its VALUE: a bare <c>readKeyboard:</c> label
    /// would be satisfied by <c>readKeyboard: Keyboard.GetState</c>, which is the default it exists to
    /// replace.</summary>
    private static readonly Regex OverlaySeamArgument = new(
        @"\breadKeyboard\s*:\s*DemoKeyboard\s*\.\s*Read\b", RegexOptions.Compiled);

    /// <summary>The demos' keyboard gate as an argument/initialiser VALUE — what every engine seam whose
    /// default is the hardware must be handed.</summary>
    private static readonly Regex SeamValue = new(@"\bDemoKeyboard\s*\.\s*Read\b", RegexOptions.Compiled);

    private static readonly Regex KeyboardSystemDeclaration = new(
        @"\bclass\s+(\w+)\s*:\s*AKeyboardInputHandlingSystem\b", RegexOptions.Compiled);

    private static readonly Regex EngageCall = new(@"\bDemoKeyboard\.Engage\s*\(", RegexOptions.Compiled);

    /// <summary>Every <c>AKeyboardInputHandlingSystem</c> subclass the ENGINE declares. A demo can
    /// construct one without declaring it (<c>DefaultEditorKeys</c>, built by <c>DemoEditor</c>), so the
    /// engaged-subclass lint seeds its set from here: matching only demo-declared subclasses left the
    /// editor's key surface — six readers wide — outside the check the premise claims covers it.</summary>
    private static readonly Lazy<IReadOnlyCollection<string>> EngineKeyboardSystems = new(() =>
    {
        var root = Path.Combine(GameTestRunner.RepoRoot(), "MonoDreams");
        var types = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative.Split(Path.DirectorySeparatorChar).Any(BuildOutputDirectories.Contains)) continue;

            foreach (Match match in KeyboardSystemDeclaration.Matches(File.ReadAllText(path)))
                types.Add(match.Groups[1].Value);
        }

        Assert.Contains("DefaultEditorKeys", types);
        return types;
    });

    /// <summary>
    /// Every value in <paramref name="source"/> that the next run will not reproduce. RNGs are matched on
    /// the TYPE rather than on one syntactic shape, because the shapes that evade a shape-matcher are
    /// exactly the ones a careless author writes: <c>new Random(Environment.TickCount)</c> (an argument,
    /// and still unpinned), <c>Random? _rng = new();</c> (nullable), <c>_rng = new();</c> in a constructor
    /// body (declaration and construction on different lines), <c>public Random Rng { get; } = new();</c>
    /// and <c>_rng ??= new();</c>. Wallclock/GUID reads are matched too — the failure they cause is the
    /// same one.
    /// </summary>
    private static IReadOnlyList<(int Line, string Snippet, string Why)> NondeterministicSources(string source) =>
        NondeterministicSources(source, CensusScope.Of([source]));

    private static IReadOnlyList<(int Line, string Snippet, string Why)> NondeterministicSources(
        string source, CensusScope scope)
    {
        // Set-wide: a Random-typed member declared in one scanned file is constructed from another.
        var randomNames = scope.RandomNames;
        // File-scoped: a bare seed name resolves against the file that USES it (see CensusScope).
        var localConstants = LocalConstants(source);

        var findings = new List<(int Line, string Snippet, string Why)>();

        foreach (Match match in SharedRandom.Matches(source))
            findings.Add((LineOf(source, match.Index), match.Value,
                "Random.Shared is process-wide and seeded from entropy"));

        foreach (Match match in RandomConstruction.Matches(source))
        {
            var arguments = ArgumentsAt(source, match.Index + match.Length - 1);
            if (IsConstantSeed(arguments, scope, localConstants)) continue;
            findings.Add((LineOf(source, match.Index), $"new Random({arguments.Trim()})", WhySeed(arguments)));
        }

        foreach (Match match in TargetTypedConstruction.Matches(source))
        {
            if (!TargetsRandom(TypeWindow(source, match.Index), randomNames)) continue;
            var arguments = ArgumentsAt(source, match.Index + match.Length - 1);
            if (IsConstantSeed(arguments, scope, localConstants)) continue;
            findings.Add((LineOf(source, match.Index), LineText(source, match.Index), WhySeed(arguments)));
        }

        foreach (Match match in EntropyRead.Matches(source))
            findings.Add((LineOf(source, match.Index), LineText(source, match.Index),
                $"'{match.Value.Trim()}' reads the wallclock / process entropy — its value differs per run " +
                "exactly as an unseeded RNG's sequence does"));

        return findings.OrderBy(finding => finding.Line).ToList();
    }

    private static string WhySeed(string arguments) =>
        arguments.Trim().Length == 0 ? "no seed" : "the seed is not a compile-time integer constant";

    /// <summary>Whether the text a bare <c>new(…)</c> takes its type from names <c>Random</c>: either the
    /// type itself (a declaration or a return type on the same line) or a name this file declared as
    /// <c>Random</c>-typed.</summary>
    private static bool TargetsRandom(string window, IReadOnlySet<string> randomNames) =>
        RandomTypeMention.IsMatch(window)
        || Identifier.Matches(window).Any(match => randomNames.Contains(match.Value));

    /// <summary>Whether a constructor argument list is a seed the next run will reproduce: an expression
    /// built only from integer literals, cast/overflow keywords and names that resolve to an integer
    /// constant of the scanned sources (<c>CameraHitSystem.ShakeJitterSeed</c>, <c>Seed + 1</c>,
    /// <c>unchecked((int)0x5EED)</c>, a <c>seed:</c>-labelled literal).</summary>
    private static bool IsConstantSeed(string arguments, CensusScope scope, IReadOnlySet<string> localConstants)
    {
        var argument = NamedArgumentLabel.Replace(arguments.Trim(), string.Empty).Trim();
        if (argument.Length == 0) return false;
        if (argument.Contains(',')) return false; // Random takes one argument; anything else is not it

        foreach (var raw in argument.Split(SeedOperators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            if (IntegerLiteral.IsMatch(token)) continue;
            if (SeedKeywords.Contains(token, StringComparer.Ordinal)) continue;
            if (scope.Resolves(token, localConstants)) continue;
            return false;
        }

        return true;
    }

    /// <summary>The text between the parenthesis at <paramref name="openParenIndex"/> and its MATCHING
    /// close — so a nested call (<c>new Random(Guid.NewGuid().GetHashCode())</c>) is read whole rather
    /// than slipping past a regex that stops at the first inner parenthesis.</summary>
    private static string ArgumentsAt(string source, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < source.Length; i++)
        {
            if (source[i] == '(') depth++;
            else if (source[i] == ')' && --depth == 0) return source[(openParenIndex + 1)..i];
        }

        return source[(openParenIndex + 1)..];
    }

    // ─── registry + helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>Strips <c>//</c> line comments (the <c>EditorThemeLintTests</c> idiom) so a lint that
    /// forbids a token never trips on prose naming it.</summary>
    private static string StripLineComments(string source) =>
        Regex.Replace(source, @"//[^\n]*", "");

    /// <summary>The exclusion list rendered for a failure message — the reason is the whole point of an
    /// entry, so a guard that fires prints it instead of only the screen name.</summary>
    private static string DescribeExclusions() =>
        Excluded.Count == 0
            ? "(none)"
            : string.Join("; ", Excluded.Select(entry => $"'{entry.Key}' ({entry.Value.Cause}) — {entry.Value.Reason}"));

    /// <summary>1-based line of a character index, so a failure names file:line.</summary>
    private static int LineOf(string source, int index) =>
        source.Take(index).Count(c => c == '\n') + 1;

    /// <summary>Start index of the line containing <paramref name="index"/>.</summary>
    private static int LineStart(string source, int index) =>
        index <= 0 ? 0 : source.LastIndexOf('\n', index - 1) + 1;

    /// <summary>The line containing <paramref name="index"/>, trimmed — a readable snippet for a failure
    /// whose match is a bare <c>new(</c> that says nothing on its own.</summary>
    private static string LineText(string source, int index)
    {
        var text = RawLine(source, index).Trim();
        return text.Length <= 120 ? text : text[..120] + "…";
    }

    /// <summary>The whole line containing <paramref name="index"/>, untruncated — what a lint matches a
    /// pattern against, as opposed to what it prints.</summary>
    private static string RawLine(string source, int index)
    {
        var start = LineStart(source, index);
        var end = source.IndexOf('\n', index);
        return source[start..(end < 0 ? source.Length : end)];
    }

    /// <summary>The text a bare target-typed <c>new(…)</c> takes its TYPE from, as far as a source scan
    /// can see it: the ENCLOSING STATEMENT up to the construction (back to the previous <c>;</c>,
    /// <c>{</c> or <c>}</c>), widened to at least the current line's prefix — a property's
    /// <c>{ get; }</c> puts a statement boundary between the type and its initialiser
    /// (<c>public Random Rng { get; } = new();</c>), while a wrapped collection expression
    /// (<c>Random[] _rngs =\n[\n    new(),\n]</c>) puts the type several lines up. Deliberately NOT the
    /// whole file, and capped in length — a <c>Random</c> declared at the top of a long method must not
    /// make every unrelated <c>new()</c> below it an RNG.</summary>
    private static string TypeWindow(string source, int index)
    {
        var statementStart = index <= 0 ? 0 : source.LastIndexOfAny(StatementBoundaries, index - 1) + 1;
        var start = Math.Min(statementStart, LineStart(source, index));
        return source[Math.Max(start, index - TypeWindowLimit)..index];
    }

    /// <summary>Characters that end the statement before a target-typed <c>new(…)</c>.</summary>
    private static readonly char[] StatementBoundaries = [';', '{', '}'];

    /// <summary>Longest look-back <see cref="TypeWindow"/> will take, so a statement that spans a whole
    /// initialiser block cannot type an unrelated construction from a <c>Random</c> far above it.</summary>
    private const int TypeWindowLimit = 400;

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

    /// <summary>Cross-checks the parsed registry against what <c>Game1</c> actually registers: every
    /// <c>RegisterScreen</c> call must name a <c>DemoScreens</c> constant, and the constants it names
    /// must be the ones parsed — otherwise a screen is bootable while invisible to the coverage
    /// guard.</summary>
    private static void AssertRegistryMatchesTheBootableSet(IReadOnlySet<string> registered)
    {
        var path = Path.Combine(GameTestRunner.RepoRoot(), "MonoDreams.Demos", "Game1.cs");
        Assert.True(File.Exists(path), $"demo host not found at {path}");
        var source = StripLineComments(File.ReadAllText(path));

        var calls = Regex.Matches(source, @"RegisterScreen\s*\(").Count;
        var viaConstant = Regex.Matches(source, @"RegisterScreen\s*\(\s*DemoScreens\.(\w+)")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.True(
            calls == viaConstant.Count,
            $"Game1 makes {calls} RegisterScreen call(s) but only {viaConstant.Count} name a DemoScreens " +
            "constant. A screen registered from a raw literal (or a constant declared elsewhere) is " +
            "bootable while never entering the registry this guard scans — register it through " +
            "DemoScreens so the precheck's scope keeps covering it.");

        Assert.True(
            viaConstant.Count == registered.Count,
            $"Game1 registers {viaConstant.Count} screen(s) but DemoScreens.cs declares {registered.Count} " +
            "id(s). Either an id is declared and never booted (the theory would run a screen the host " +
            "cannot load) or the parse missed one.");
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
        // ...the composed op driver proves the plan loaded...
        result.AssertLogContains("editor.opDriver");
        // ...and the protocol actually ENGAGED — the screen pinned both hardware legs. Without this
        // line the run silently degrades to "deterministic unless the developer touches the keyboard",
        // which shows up as an intermittent byte diff instead of a failing test.
        result.AssertLogContains(InputProtocolEngagedLog);
        // ...and Play must have resumed the sim, or this would compare a frozen scene.
        result.AssertLogContains("Transport: Playing.");
        result.AssertLogContains($"Headless run complete after {Frames} frames");
        // A blank frame pair would be byte-identical and prove nothing.
        result.AssertScreenshotNonBlank();
        return result;
    }
}
