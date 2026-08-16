using System.Text.RegularExpressions;
using MonoDreams.Cli.Commands;
using MonoDreams.Cli.Manifest;
using MonoDreams.Cli.Resolver;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Issue #83 — the manifest-honesty check: for EVERY module in the registry, cook the recipe exactly as a
/// fresh user would (<c>monodreams init</c> → <c>monodreams add &lt;module&gt;</c> → <c>dotnet build</c>) and
/// require the result to compile. <c>add</c> installs the module plus its <b>declared</b> transitive
/// dependencies and nothing else, so a module whose source imports a namespace no declared dependency owns
/// fails here — the bug class #82 fixed by hand for <c>collision</c>, which no dev machine can reproduce
/// (every checkout has all 14 modules on disk, so the missing ingredient is always in the pantry).
///
/// <para><b>How to run it.</b> The suite is opt-in because each case is a real NuGet restore + build
/// (~30-60s); <c>dotnet test</c> stays fast without it.
/// <code>
/// MONODREAMS_MANIFEST_HONESTY=1 dotnet test MonoDreams.Cli.Tests/ --filter FullyQualifiedName~ManifestHonesty
/// MONODREAMS_MANIFEST_HONESTY=1 MONODREAMS_HONESTY_MODULE=collision dotnet test MonoDreams.Cli.Tests/ --filter FullyQualifiedName~ManifestHonesty
/// </code>
/// CI runs it as one job per module (<c>.github/workflows/manifest-honesty.yml</c>), which is where the
/// parallelism and the NuGet cache live — locally the cases share one xunit collection and run one at a
/// time, because concurrent restores of the same packages race each other's <c>project.nuget.cache</c>
/// writes.</para>
///
/// <para><b>Scope.</b> Desktop backend only: the web head needs the <c>wasm-tools</c> workload, which is not
/// a prerequisite of this repo's gate (see the class-level note on
/// <see cref="ScaffolderBuildTests.Init_Web_ThenAdd_WiresKniBackendAndWebHost"/>). Per-platform *package*
/// wiring is covered in-process there.</para>
/// </summary>
[Collection("scaffolder-build")]
public class ManifestHonestyTests
{
    /// <summary>Set to <c>1</c> to actually run the builds (see the class remarks).</summary>
    private const string EnabledVar = "MONODREAMS_MANIFEST_HONESTY";

    /// <summary>Set to a module name to run that one case only — how the CI matrix shards the suite.</summary>
    private const string ModuleFilterVar = "MONODREAMS_HONESTY_MODULE";

    /// <summary>
    /// The <b>compile floor</b>: extra modules installed alongside the module under test.
    ///
    /// <para><c>monodreams init</c> installs <c>foundation</c>, and a foundation-only project does not
    /// compile today — <c>foundation/Screen/ScreenController.cs</c> takes <c>ViewportManager</c> and
    /// <c>Camera</c> (both owned by <c>rendering</c>) as constructor parameters, and <c>rendering</c> in turn
    /// reads <c>DynamicTextComponent.DefaultLineSpacing</c> from <c>rendering-text</c>. Neither can be fixed
    /// by declaring the dependency (both would be cycles — <c>rendering</c> depends on <c>foundation</c>,
    /// <c>rendering-text</c> on <c>rendering</c>); they need the coupling moved in code, which is tracked
    /// separately. Until then every case would fail for a reason that has nothing to do with the module
    /// under test, so the check installs the smallest set that compiles — the declared closure of
    /// <c>rendering-text</c>, i.e. foundation + rendering + rendering-text.</para>
    ///
    /// <para>The floor's own members are checked <b>strictly</b> (declared closure only, no floor), so their
    /// gaps stay visible as known gaps below instead of being papered over by the workaround they cause.
    /// The cost of the floor is that a module whose closure excludes <c>rendering</c>/<c>rendering-text</c>
    /// (<c>physics</c>, <c>collision</c>, <c>audio</c>) could import a rendering namespace undetected; that
    /// hole closes by itself the day the floor does.</para>
    /// </summary>
    private const string FloorModule = "rendering-text";

    /// <summary>
    /// Modules that do NOT compile from their declared dependencies today, each listed with the symbols its
    /// gap makes the compiler complain about. An entry is a promise, not an excuse — the case fails when:
    /// <list type="bullet">
    /// <item>the build now succeeds (the gap was fixed → delete the entry);</item>
    /// <item>a marker stops appearing (part of the gap was fixed → narrow the entry);</item>
    /// <item><b>any</b> diagnostic the build emits is explained by no marker — a NEW gap hiding behind a
    /// known one, which is the only reason to spell the gap out instead of skipping the module.</item>
    /// </list>
    /// That last one is why the markers are matched against the <i>set of diagnostics</i>
    /// (<see cref="ErrorDiagnostics"/>) rather than against the raw log: finding a marker somewhere in the
    /// log proves the known error is still emitted, never that it is the only one. Nothing else in the
    /// registry is allowed to fail.
    ///
    /// <para>One limit is the compiler's, not the check's: Roslyn stops after the declaration phase once it
    /// has errors, so while a module is listed here a new gap reachable only from a <i>method body</i> is
    /// not compiled and therefore not reported. Gaps in declarations (field, parameter, base and return
    /// types — where an undeclared module dependency almost always shows up first) are caught immediately.
    /// The listed module's own build is the only place this applies; every unlisted module compiles fully.</para>
    ///
    /// Every entry here is a real bug a user hits on <c>monodreams add &lt;module&gt;</c>; they are catalogued
    /// in issue #83 for follow-up fixes (the manifest ones are one-line manifest edits, the cyclic ones need
    /// the coupling moved in code).
    ///
    /// <para>A marker is the symbol <b>as the compiler quotes it</b> (<c>"'Camera'"</c>, not <c>"Camera"</c>),
    /// so it matches the type the gap is about and not every longer name containing it; drop the closing
    /// quote to cover a family (<c>"'ContentImporter"</c> spans <c>ContentImporter</c>,
    /// <c>ContentImporter&lt;&gt;</c>, <c>ContentImporterAttribute</c>, <c>ContentImporterContext</c>).</para>
    /// </summary>
    private static readonly Dictionary<string, KnownGap> KnownGaps = new(StringComparer.Ordinal)
    {
        ["foundation"] = new(
            Why: "ScreenController takes ViewportManager + Camera (owned by `rendering`), which foundation "
                 + "cannot declare without a cycle — the coupling has to move in code.",
            Markers: new[] { "'ViewportManager'", "'Camera'", "'Renderer'" }),

        ["rendering"] = new(
            Why: "DrawComponent + MasterRenderSystem read DynamicTextComponent.DefaultLineSpacing (owned by "
                 + "`rendering-text`, which depends on rendering) — the constant has to move in code.",
            Markers: new[] { "'DynamicTextComponent'" }),

        // ["ui"] was a known gap (undeclared `cursor` dependency) until PR #112 declared it —
        // the module now compiles from its declared dependencies alone and the check guards it.

        ["dialogue"] = new(
            Why: "the YarnSpinner content-pipeline importer needs the MonoGame.Framework.Content.Pipeline "
                 + "package, which no nugetDependencies entry declares. (Its second gap — the `cursor` "
                 + "dependency inherited through ui — closed when PR #112 declared `cursor` on ui.)",
            Markers: new[]
            {
                "'Pipeline'", "'TargetPlatform'", "'ContentImporter", "'ContentProcessor",
                "'ContentTypeWriter", "'ContentWriter'",                           // the importer's base types
            }),

        ["level-editor"] = new(
            Why: "editor systems use CameraComponent / CameraFollowTargetComponent (owned by `camera`), which "
                 + "module.json does not declare. Acyclic: declaring `camera` fixes it.",
            Markers: new[] { "'CameraComponent'", "'CameraFollowTargetComponent'" }),
    };

    /// <summary>Every module the registry publishes — the check covers the registry, not a hand-kept list.</summary>
    public static TheoryData<string> Modules
    {
        get
        {
            var only = Environment.GetEnvironmentVariable(ModuleFilterVar);
            var data = new TheoryData<string>();
            foreach (var entry in Registry.Load(CliTestSupport.FindRepoRoot()).Index.Modules)
                if (string.IsNullOrEmpty(only) || string.Equals(only, entry.Name, StringComparison.Ordinal))
                    data.Add(entry.Name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task Module_CompilesFromItsDeclaredDependenciesAlone(string module)
    {
        if (!Enabled(module)) return;

        var repo = CliTestSupport.FindRepoRoot();
        var workDir = CliTestSupport.NewTempDir($"honesty-{module}");
        var name = ProjectName(module);
        var projectDir = Path.Combine(workDir, name);

        try
        {
            await Runner.RunInitAsync(name, projectDir, "desktop", repo);
            Assert.True(File.Exists(Path.Combine(projectDir, $"{name}.sln")), "init did not produce the .sln");

            // `add <module>` and nothing else: the CLI resolves the DECLARED closure itself. The floor rides
            // along only for modules outside its own closure (see FloorModule).
            var toAdd = NeedsFloor(repo, module) ? new[] { module, FloorModule } : new[] { module };
            await Runner.RunAddAsync(toAdd, presetName: null, projectPath: projectDir, dryRun: false, registryPath: repo);

            var (exitCode, output) = CliTestSupport.BuildScaffoldedProject(Path.Combine(projectDir, $"{name}.sln"));

            if (!KnownGaps.TryGetValue(module, out var gap))
            {
                if (exitCode != 0)
                {
                    var log = CliTestSupport.DumpBuildLog($"honesty-{module}", module, output);
                    Assert.Fail(
                        $"`monodreams init` + `monodreams add {module}` + `dotnet build` failed (exit {exitCode}).\n"
                        + $"Either MonoDreams/{module}/module.json under-declares what its source needs "
                        + "(add the owning module to `dependencies`, or the package to `nugetDependencies`), or "
                        + $"`monodreams add` is broken. Full log: {log}\n{CliTestSupport.Tail(output, 6000)}");
                }
                return;
            }

            Assert.False(exitCode == 0,
                $"'{module}' is listed as a known manifest gap but now builds from its declared dependencies "
                + $"alone. The gap is fixed — delete its entry from {nameof(KnownGaps)} so the check guards it "
                + "from now on.");

            var diagnostics = ErrorDiagnostics(output);
            if (diagnostics.Count == 0)
            {
                var emptyLog = CliTestSupport.DumpBuildLog($"honesty-{module}", module, output);
                Assert.Fail(
                    $"'{module}' is listed as a known manifest gap and the build did fail (exit {exitCode}), but "
                    + "the log carries no compiler diagnostic to match the gap against — it broke before "
                    + $"compiling (restore, timeout, tooling). Full log: {emptyLog}\n{CliTestSupport.Tail(output, 6000)}");
            }

            // EVERY diagnostic has to be one of the known gap's, not merely SOME of them: a build that still
            // reports the known error AND a new one is a new gap hiding behind a known one.
            var unexplained = Unexplained(diagnostics, gap.Markers);
            if (unexplained.Count > 0)
            {
                var log = CliTestSupport.DumpBuildLog($"honesty-{module}", module, output);
                Assert.Fail(
                    $"'{module}' fails for MORE than its known gap ({gap.Why}) — {unexplained.Count} of "
                    + $"{diagnostics.Count} diagnostics are explained by no marker in its {nameof(KnownGaps)} "
                    + "entry. A new gap is hiding behind the known one: fix it, or — if it is genuinely part of "
                    + $"the known gap — add the symbol it names to Markers. Full log: {log}\n  "
                    + string.Join("\n  ", unexplained));
            }

            foreach (var marker in gap.Markers)
                Assert.True(diagnostics.Any(d => d.Contains(marker, StringComparison.Ordinal)),
                    $"'{module}' no longer fails on {marker}, so its known gap ({gap.Why}) has shrunk: that "
                    + $"part is fixed while the rest is not. Narrow the {nameof(KnownGaps)} entry to what still "
                    + $"breaks.\nDiagnostics:\n  {string.Join("\n  ", diagnostics)}");
        }
        finally { CliTestSupport.TryDeleteWorkDir(projectDir); }
    }

    /// <summary>
    /// Cheap, always-on guard on the matching rule the known-gap list rests on: a gap entry only excuses the
    /// diagnostics it names, so a build that emits the known error <b>plus</b> a new one is still a failure.
    /// Asserting "the known marker appears somewhere in the log" would pass that build — the very hole this
    /// covers — and the expensive suite is opt-in, so the rule is proven here instead, in every
    /// <c>dotnet test</c>.
    /// </summary>
    [Fact]
    public void ANewGapHidingBehindAKnownOne_IsNotExplainedByTheKnownMarker()
    {
        const string missing = "error CS0246: The type or namespace name '{0}' could not be found "
                               + "(are you missing a using directive or an assembly reference?)";
        var log = string.Join('\n', new[]
        {
            // A real log: absolute temp paths in front, MSBuild's [project] behind, and every diagnostic
            // printed twice — once where it happens, once in the end-of-build summary.
            $"  /tmp/md/HonestyUi/Ui.cs(70,13): {string.Format(missing, "CursorInputComponent")} [/tmp/md/HonestyUi/HonestyUi.csproj]",
            $"  /tmp/md/HonestyUi/Text.cs(9,5): {string.Format(missing, "DynamicTextComponent")} [/tmp/md/HonestyUi/HonestyUi.csproj]",
            "  Build FAILED.",
            $"  /tmp/md/HonestyUi/Ui.cs(70,13): {string.Format(missing, "CursorInputComponent")} [/tmp/md/HonestyUi/HonestyUi.csproj]",
            $"  /tmp/md/HonestyUi/Text.cs(9,5): {string.Format(missing, "DynamicTextComponent")} [/tmp/md/HonestyUi/HonestyUi.csproj]",
            "      2 Error(s)",
        });

        var diagnostics = ErrorDiagnostics(log);

        // Path prefix and [project] suffix dropped, the summary repeat collapsed onto the original.
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.StartsWith("CS0246: ", d, StringComparison.Ordinal));

        // The known gap ('cursor') no longer explains the whole build: the new one is reported.
        var hidden = Unexplained(diagnostics, new[] { "'CursorInputComponent'" });
        Assert.Contains("'DynamicTextComponent'", Assert.Single(hidden), StringComparison.Ordinal);

        // Naming both leaves nothing unexplained — the state a gap entry has to be kept in.
        Assert.Empty(Unexplained(diagnostics, new[] { "'CursorInputComponent'", "'DynamicTextComponent'" }));

        // And a marker is matched as the compiler quotes the symbol, so it cannot spill onto a longer name.
        Assert.Equal(2, Unexplained(diagnostics, new[] { "'Cursor'", "'DynamicText'" }).Count);
    }

    /// <summary>
    /// Cheap, always-on guard on the known-gap list itself: an entry naming a module the registry no longer
    /// publishes (renamed, split, deleted) would silently stop covering anything, and the expensive suite
    /// only notices when someone runs it. This runs in every <c>dotnet test</c>.
    /// </summary>
    [Fact]
    public void KnownGaps_AndFloor_NameModulesTheRegistryStillPublishes()
    {
        var registry = Registry.Load(CliTestSupport.FindRepoRoot());
        var published = registry.Index.Modules.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(FloorModule, published);
        foreach (var module in KnownGaps.Keys)
            Assert.True(published.Contains(module),
                $"{nameof(KnownGaps)} names '{module}', which is not a module in the registry any more — "
                + "update or remove the entry.");
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Matches one MSBuild/compiler error line, from the code onwards — the <c>file(line,col):</c>
    /// prefix in front of it is a temp path that says nothing about the gap.</summary>
    private static readonly Regex ErrorLine =
        new(@"\berror\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<message>.+)$", RegexOptions.Compiled);

    /// <summary>MSBuild appends the originating project to every diagnostic it forwards: <c>… [/tmp/X.csproj]</c>.</summary>
    private static readonly Regex ProjectSuffix = new(@"\s*\[[^\]]*\]\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Every distinct error in a build log, normalised to <c>CODE: message</c>. The path prefix and MSBuild's
    /// trailing <c>[project]</c> are dropped, so the same error — printed once where it happens and again in
    /// the end-of-build summary — collapses into one entry, and two runs in different temp directories
    /// produce the same set. Non-compiler errors (restore, MSB…) are kept: a known gap does not excuse them.
    /// </summary>
    private static List<string> ErrorDiagnostics(string buildOutput)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in buildOutput.Split('\n'))
        {
            var match = ErrorLine.Match(line.TrimEnd());
            if (!match.Success) continue;
            var message = ProjectSuffix.Replace(match.Groups["message"].Value.Trim(), string.Empty).Trim();
            var diagnostic = $"{match.Groups["code"].Value}: {message}";
            if (seen.Add(diagnostic)) ordered.Add(diagnostic);
        }
        return ordered;
    }

    /// <summary>
    /// The diagnostics none of <paramref name="markers"/> accounts for — what turns "the known error is still
    /// there" into "the known error is all there is".
    /// </summary>
    private static List<string> Unexplained(IEnumerable<string> diagnostics, IReadOnlyList<string> markers) =>
        diagnostics.Where(d => !markers.Any(m => d.Contains(m, StringComparison.Ordinal))).ToList();

    /// <summary>What the module under test gets installed on top of: the floor, unless it IS the floor.</summary>
    private static bool NeedsFloor(string repo, string module) =>
        !DependencyResolver
            .Resolve(Registry.Load(repo), new[] { FloorModule }, Array.Empty<string>(), Platform.Desktop)
            .Contains(module, StringComparer.Ordinal);

    /// <summary>Module names are kebab-case; project names feed a &lt;RootNamespace&gt;, so strip the dashes.</summary>
    private static string ProjectName(string module) => "Honesty" + module.Replace("-", "");

    /// <summary>
    /// Follows this suite's opt-in gate and the repo's Windows guard (the build launcher is
    /// <c>/usr/bin/env -i</c>). A skipped case prints why — xunit 2.x has no dynamic skip, so a silent pass
    /// would be the only alternative.
    /// </summary>
    private static bool Enabled(string module)
    {
        if (!CliTestSupport.CanBuildScaffoldedProjects())
        {
            Console.WriteLine($"[manifest-honesty] skipped '{module}': scaffolded-project builds need a Unix host.");
            return false;
        }
        if (Environment.GetEnvironmentVariable(EnabledVar) != "1")
        {
            Console.WriteLine($"[manifest-honesty] skipped '{module}': set {EnabledVar}=1 to run the scaffold+add+build check.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// A module that does not build from its declared dependencies yet.
    /// <paramref name="Why"/> is the gap in one sentence; <paramref name="Markers"/> are the symbols its
    /// diagnostics name — <b>every</b> error the build emits must contain one of them, so the set has to be
    /// complete, not illustrative (see <see cref="KnownGaps"/>).
    /// </summary>
    private sealed record KnownGap(string Why, string[] Markers);
}
