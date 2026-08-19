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
///   <c>AKeyboardInputHandlingSystem</c> in the screen). Without the mouse leg a headless run
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
/// exclusion is data, not prose: it lives in <see cref="Excluded"/> and
/// <see cref="Precheck_CoversEveryDemoScreen_OrNamesTheExclusionAndWhy"/> fails the moment the
/// covered set plus the excluded set stops being <i>every</i> screen the host can boot — so a
/// screen added later, or one quietly dropped from the run list, cannot shrink the precheck in
/// silence.</para>
///
/// <para>"Physics is the only unseeded RNG <b>in the demo screen sources</b>" is likewise checked
/// rather than asserted in prose:
/// <see cref="Precheck_CoveredScreensSeedEveryRandom_AndTheExclusionReasonStillHolds"/> scans
/// every <c>.cs</c> file in each covered screen's demo directory. That scope is the claim's scope,
/// and it is narrower than "the whole demo surface": the engine systems a demo screen composes
/// (cursor, hierarchy, layout, camera) are not scanned here — they carry no RNG today, and the
/// lint that would cover them belongs to the engine, not to this precheck. A DORMANT RNG still
/// counts as a failure — the camera demo carried one (its hit-shake jitter) that only wakes when
/// the dot enters a hit square, so today's green run said nothing about tomorrow's op plan, while
/// every record here named physics as the single source and would have sent the debugger to the
/// wrong screen. It is seeded now (<c>CameraHitSystem.ShakeJitterSeed</c>), and the same test
/// asserts the physics exclusion's stated reason still holds, so seeding that screen without
/// widening <see cref="Covered"/> fails too.</para>
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

    /// <summary>Screens knowingly outside the precheck, each with the reason it cannot join yet. An
    /// entry here is a debt, not a waiver: the reason is printed by the coverage guard's failure
    /// message and by any C7 gate that quotes this set.</summary>
    private static readonly Dictionary<string, string> Excluded = new()
    {
        ["physics"] = "PhysicsDemoScreen builds its scene from an unseeded Random (ball radius, spawn " +
                      "point, initial velocity), so the scene CONTENT differs per process — seed it " +
                      "before this screen can carry a pixel-identity claim.",
    };

    /// <summary>Where each registered demo screen's source lives, relative to the repo root — a
    /// DIRECTORY, scanned recursively, not a single file: a demo split across two files (systems
    /// extracted next to the screen) would otherwise leave half of itself unscanned by
    /// <see cref="Precheck_CoveredScreensSeedEveryRandom_AndTheExclusionReasonStillHolds"/> and
    /// <see cref="Precheck_EveryDemoScreenRoutesHardwareInputThroughTheProtocol"/>.</summary>
    private static readonly Dictionary<string, string> ScreenSources = new()
    {
        ["launcher"] = Path.Combine("MonoDreams.Demos", "Screens"),
        ["camera"] = Path.Combine("MonoDreams", "camera", "demo"),
        ["physics"] = Path.Combine("MonoDreams", "physics", "demo"),
        ["dialogue"] = Path.Combine("MonoDreams", "dialogue", "demo"),
        ["ui"] = Path.Combine("MonoDreams", "ui", "demo"),
        ["audio"] = Path.Combine("MonoDreams", "audio", "demo"),
    };

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
    /// screen's demo sources must therefore contain no unseeded <c>Random</c> — dormant or not: an RNG
    /// that today's pinned run never reaches (the camera demo's hit-shake jitter fires only once the dot
    /// enters a hit square) still reds a future run the moment an op plan wakes it — and then
    /// <see cref="Excluded"/>, the C8 premise and the contract all point the debugger at physics, the one
    /// screen that is not to blame.
    ///
    /// <para>The converse is checked too: a screen excluded FOR an unseeded RNG must still have one.
    /// Seeding physics without moving it into <see cref="Covered"/> would leave the precheck five
    /// screens wide with nothing left to justify it.</para>
    /// </summary>
    [Fact]
    public void Precheck_CoveredScreensSeedEveryRandom_AndTheExclusionReasonStillHolds()
    {
        var registered = RegisteredDemoScreens();

        var unmapped = registered.Where(screen => !ScreenSources.ContainsKey(screen)).ToList();
        Assert.True(
            unmapped.Count == 0,
            $"demo screen(s) [{string.Join(", ", unmapped)}] have no entry in {nameof(ScreenSources)}, so " +
            "nothing scans them for unseeded randomness. Map them to their demo source directory.");

        var staleSources = ScreenSources.Keys.Where(screen => !registered.Contains(screen)).ToList();
        Assert.True(
            staleSources.Count == 0,
            $"[{string.Join(", ", staleSources)}] is mapped in {nameof(ScreenSources)} but no longer " +
            "registered in DemoScreens.cs — the scan is reading a screen the host cannot boot.");

        foreach (var screen in Covered)
        {
            foreach (var (path, source) in ReadScreenSources(screen))
            {
                var offenders = UnseededRandoms(source);
                if (offenders.Count == 0) continue;

                var (line, snippet, why) = offenders[0];
                Assert.Fail(
                    $"'{screen}' is run by {nameof(Demo_RunTwiceHeadless_ProducesByteIdenticalPngs)} but " +
                    $"{path}:{line} creates an RNG nothing pins the sequence of ('{snippet}' — {why}). Even " +
                    "while nothing consumes it, it is a byte-identity failure waiting for the input that " +
                    "reaches it, and every record here names physics as the only unseeded RNG in a demo " +
                    "screen source. Seed it with a COMPILE-TIME CONSTANT (CameraHitSystem's ShakeJitterSeed " +
                    "is the pattern — a wallclock or entropy seed is no better than none), or move the " +
                    $"screen into {nameof(Excluded)} with the reason it cannot be seeded.");
            }
        }

        foreach (var (screen, reason) in Excluded)
        {
            if (!reason.Contains("unseeded", StringComparison.OrdinalIgnoreCase)) continue;

            var sources = ReadScreenSources(screen);
            Assert.True(
                sources.Any(file => UnseededRandoms(file.Source).Count > 0),
                $"'{screen}' is excluded from the precheck because of an unseeded Random ({reason}), but " +
                $"no source under {ScreenSources[screen]} has one any more. Move it into " +
                $"{nameof(Covered)} (and re-run the precheck) rather than leaving the byte-identity claim " +
                "narrower than the code now allows.");
        }
    }

    /// <summary>
    /// The protocol's INPUT leg, linted rather than trusted. Every demo screen must read the keyboard
    /// through the demos' shared gate (<c>DemoKeyboard.Read</c>) and the mouse through
    /// <c>CursorInputSystem</c>, and every screen that builds a cursor pipeline must engage the
    /// protocol (<c>DemoKeyboard.Engage</c>) — that call is what flips both legs and what emits the
    /// line <see cref="RunOnce"/> asserts.
    ///
    /// <para>Without this lint the keyboard leg degrades exactly the way the mouse leg used to: a
    /// screen (or a new system inside one) calls <c>Keyboard.GetState()</c> directly, every run stays
    /// green on a machine with no key held, and the byte-identity claim quietly becomes conditional on
    /// the developer's hands. Line comments are stripped first, so prose naming the forbidden call is
    /// never a false positive (unlike the RNG census above, which scans comments on purpose — a
    /// commented-out RNG is a trap the next author copies).</para>
    /// </summary>
    [Fact]
    public void Precheck_EveryDemoScreenRoutesHardwareInputThroughTheProtocol()
    {
        foreach (var screen in RegisteredDemoScreens())
        {
            var sources = ReadScreenSources(screen);
            var engages = false;
            var buildsCursorPipeline = false;

            foreach (var (path, source) in sources)
            {
                var code = StripLineComments(source);

                var rawRead = Regex.Match(code, @"\b(?:Keyboard|Mouse)\.GetState\s*\(");
                Assert.False(
                    rawRead.Success,
                    $"'{screen}' reads the hardware directly at {path}:{LineOf(code, rawRead.Index)} " +
                    $"('{rawRead.Value}'). The deterministic-input protocol pins input at ONE seam: read " +
                    "the keyboard through DemoKeyboard.Read() (and the mouse through CursorInputSystem, " +
                    "whose SkipHardwareRead the protocol sets). A direct read is invisible until a key " +
                    "held during one of two byte-identity runs moves the scene in that run only.");

                if (code.Contains("new CursorInputSystem(", StringComparison.Ordinal)) buildsCursorPipeline = true;
                if (code.Contains("DemoKeyboard.Engage(", StringComparison.Ordinal)) engages = true;
            }

            Assert.True(
                !buildsCursorPipeline || engages,
                $"'{screen}' builds a CursorInputSystem but no source under {ScreenSources[screen]} calls " +
                "DemoKeyboard.Engage(...). Engaging the protocol is what pins BOTH hardware legs and what " +
                $"logs '{InputProtocolEngagedLog}' — the line {nameof(RunOnce)} asserts. A screen that " +
                "pins only the cursor passes every run until a keypress lands in one of them.");
        }
    }

    /// <summary>
    /// The census's own contract, on synthetic sources — a scan whose escapes are unknown is a scan
    /// whose green run means nothing, and every form below is one a shape-matching regex accepted while
    /// the records claimed enforcement. Seeded cases prove the converse: the scan must not fail a demo
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
    // Pinned — none of these may fail the scan.
    [InlineData("var rng = new Random(7);", false)]
    [InlineData("var rng = new Random(-1);", false)]
    [InlineData("private const int Seed = 7;\n    private readonly Random _rng = new(Seed);", false)]
    [InlineData("private const int Seed = 7;\n    private readonly Random _rng = new Random(Other.Seed);", false)]
    [InlineData("private Vector2 RandomVelocity() => Vector2.Zero;", false)]
    public void RngCensus_MatchesOnTheType_NotOnOneSyntacticShape(string source, bool expectUnpinned)
    {
        var findings = UnseededRandoms(source);

        Assert.True(
            findings.Count > 0 == expectUnpinned,
            expectUnpinned
                ? $"the census missed an unpinned RNG in: {source}"
                : $"the census flagged a properly pinned RNG in: {source} " +
                  $"({string.Join("; ", findings.Select(f => $"{f.Snippet} — {f.Why}"))})");
    }

    /// <summary>Reads every <c>.cs</c> file of a registered screen's demo directory, failing with the
    /// mapping to fix when it moved.</summary>
    private static IReadOnlyList<(string RelativePath, string Source)> ReadScreenSources(string screen)
    {
        var relative = ScreenSources[screen];
        var full = Path.Combine(GameTestRunner.RepoRoot(), relative);
        Assert.True(
            Directory.Exists(full),
            $"demo sources for screen '{screen}' not found at {relative} — update {nameof(ScreenSources)}.");

        var files = Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(GameTestRunner.RepoRoot(), path), File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(files);
        return files;
    }

    // ─── the RNG census ──────────────────────────────────────────────────────────────────────────

    /// <summary>Integer constants declared in the scanned file — the only names allowed as a seed, so
    /// the census distinguishes <c>new Random(ShakeJitterSeed)</c> from
    /// <c>new Random(Environment.TickCount)</c> instead of accepting "it has an argument".</summary>
    private static readonly Regex IntegerConstant = new(
        @"\bconst\s+(?:int|uint|long|ulong|short|ushort|byte|sbyte)\s+(\w+)\s*=", RegexOptions.Compiled);

    /// <summary>Declarations of a <c>Random</c>-typed field/local (nullable included), so a
    /// target-typed <c>new()</c> assigned to one — inline or in a constructor body — is recognised as
    /// an RNG construction.</summary>
    private static readonly Regex RandomDeclaration = new(
        @"\b(?:System\.)?Random\??\s+(\w+)\s*[;=,)]", RegexOptions.Compiled);

    private static readonly Regex RandomConstruction = new(
        @"\bnew\s+(?:System\.)?Random\s*\(", RegexOptions.Compiled);

    private static readonly Regex TargetTypedConstruction = new(
        @"\b(\w+)\s*=\s*new\s*\(", RegexOptions.Compiled);

    private static readonly Regex SharedRandom = new(@"\b(?:System\.)?Random\.Shared\b", RegexOptions.Compiled);

    private static readonly Regex IntegerLiteral = new(@"^-?\s*(?:\d[\d_]*|0[xX][0-9a-fA-F_]+)$", RegexOptions.Compiled);

    /// <summary>
    /// Every RNG in <paramref name="source"/> whose sequence nothing pins. Matches on the TYPE rather
    /// than on one syntactic shape, because the shapes that evade a shape-matcher are exactly the ones
    /// a careless author writes: <c>new Random(Environment.TickCount)</c> (an argument, and still
    /// unpinned), <c>Random? _rng = new();</c> (nullable), and <c>_rng = new();</c> in a constructor
    /// body (the declaration and the construction on different lines).
    /// </summary>
    private static IReadOnlyList<(int Line, string Snippet, string Why)> UnseededRandoms(string source)
    {
        var constants = IntegerConstant.Matches(source).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var randomNames = RandomDeclaration.Matches(source).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var findings = new List<(int Line, string Snippet, string Why)>();

        foreach (Match match in SharedRandom.Matches(source))
            findings.Add((LineOf(source, match.Index), match.Value,
                "Random.Shared is process-wide and seeded from entropy"));

        foreach (Match match in RandomConstruction.Matches(source))
        {
            var arguments = ArgumentsAt(source, match.Index + match.Length - 1);
            if (IsConstantSeed(arguments, constants)) continue;
            findings.Add((LineOf(source, match.Index), $"new Random({arguments.Trim()})",
                arguments.Trim().Length == 0 ? "no seed" : "the seed is not a compile-time integer constant"));
        }

        foreach (Match match in TargetTypedConstruction.Matches(source))
        {
            if (!randomNames.Contains(match.Groups[1].Value)) continue;
            var arguments = ArgumentsAt(source, match.Index + match.Length - 1);
            if (IsConstantSeed(arguments, constants)) continue;
            findings.Add((LineOf(source, match.Index), $"{match.Groups[1].Value} = new({arguments.Trim()})",
                arguments.Trim().Length == 0 ? "no seed" : "the seed is not a compile-time integer constant"));
        }

        return findings.OrderBy(finding => finding.Line).ToList();
    }

    /// <summary>Whether a constructor argument list is a seed the next run will reproduce: an integer
    /// literal, or a name that resolves to an integer <c>const</c> declared in the same file
    /// (<c>CameraHitSystem.ShakeJitterSeed</c> — qualified or not).</summary>
    private static bool IsConstantSeed(string arguments, ISet<string> constants)
    {
        var argument = arguments.Trim();
        if (argument.Length == 0) return false;
        if (argument.Contains(',')) return false; // Random takes one argument; anything else is not it
        if (IntegerLiteral.IsMatch(argument)) return true;
        if (argument.Contains('(')) return false; // a call, never a constant
        return constants.Contains(argument.Split('.')[^1]);
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
            : string.Join("; ", Excluded.Select(entry => $"'{entry.Key}' — {entry.Value}"));

    /// <summary>1-based line of a character index, so a failure names file:line.</summary>
    private static int LineOf(string source, int index) =>
        source.Take(index).Count(c => c == '\n') + 1;

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
