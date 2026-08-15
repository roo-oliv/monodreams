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
    /// Modules that do NOT compile from their declared dependencies today, with the diagnostic that proves
    /// it is still the same gap. Each entry is a promise, not an excuse: the case fails if the build now
    /// succeeds (the gap was fixed → delete the entry) and it fails if the build breaks for any other reason
    /// (a NEW gap hiding behind a known one). Nothing else in the registry is allowed to fail.
    ///
    /// Every entry here is a real bug a user hits on <c>monodreams add &lt;module&gt;</c>; they are catalogued
    /// in issue #83 for follow-up fixes (the manifest ones are one-line manifest edits, the cyclic ones need
    /// the coupling moved in code).
    /// </summary>
    private static readonly Dictionary<string, KnownGap> KnownGaps = new(StringComparer.Ordinal)
    {
        ["foundation"] = new(
            Why: "ScreenController takes ViewportManager + Camera (owned by `rendering`), which foundation "
                 + "cannot declare without a cycle — the coupling has to move in code.",
            Markers: new[] { "ViewportManager" }),

        ["rendering"] = new(
            Why: "DrawComponent + MasterRenderSystem read DynamicTextComponent.DefaultLineSpacing (owned by "
                 + "`rendering-text`, which depends on rendering) — the constant has to move in code.",
            Markers: new[] { "DynamicTextComponent" }),

        ["ui"] = new(
            Why: "ui source opens MonoDreams.Component.Cursor (CursorInputComponent / CursorType) but "
                 + "module.json declares only foundation + rendering. Acyclic: declaring `cursor` fixes it.",
            Markers: new[] { "CursorInputComponent" }),

        ["dialogue"] = new(
            Why: "two gaps — the same undeclared `cursor` dependency it inherits through ui, plus the "
                 + "YarnSpinner content-pipeline importer, which needs the MonoGame.Framework.Content.Pipeline "
                 + "package no nugetDependencies entry declares.",
            Markers: new[] { "CursorInputComponent", "ContentImporter" }),

        ["level-editor"] = new(
            Why: "editor systems use CameraComponent / CameraFollowTargetComponent (owned by `camera`), which "
                 + "module.json does not declare. Acyclic: declaring `camera` fixes it.",
            Markers: new[] { "CameraComponent" }),
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

            foreach (var marker in gap.Markers)
                Assert.True(output.Contains(marker, StringComparison.Ordinal),
                    $"'{module}' failed for a DIFFERENT reason than the known gap ({gap.Why}): the build output "
                    + $"never mentions '{marker}'. A new gap is hiding behind the known one — investigate "
                    + $"before touching {nameof(KnownGaps)}.\n{CliTestSupport.Tail(output, 6000)}");
        }
        finally { CliTestSupport.TryDeleteWorkDir(projectDir); }
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

    /// <summary>A module that does not build from its declared dependencies yet, and the proof it is still that gap.</summary>
    private sealed record KnownGap(string Why, string[] Markers);
}
