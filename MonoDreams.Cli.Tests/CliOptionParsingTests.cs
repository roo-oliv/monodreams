using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Contract protection for the CLI's argument surface (issue #85), driven through
/// <see cref="Program.RunAsync"/> — the same entry point the shell uses, so the tests cover parsing,
/// binding, printed message and exit code together:
/// <list type="bullet">
///   <item>ONE canonical "where is the project" option, <c>--dir</c>, shared by <c>init</c> and <c>add</c>;
///     <c>--project</c> (what <c>add</c> used to spell it) keeps working as a hidden, deprecated alias.</item>
///   <item>An unrecognized option is an error that NAMES the option — never swallowed as a positional
///     module name or scene path, which used to produce "Module '--dir' not found".</item>
///   <item>A command that reports a user error exits non-zero (it used to print <c>error:</c> and exit 0,
///     because the value returned from Main wins over <see cref="Environment.ExitCode"/>).</item>
/// </list>
/// </summary>
[Collection("Console (non-parallel: swaps Console.Out)")]
public class CliOptionParsingTests
{
    // ---- unknown options are named, never swallowed --------------------------------------------

    [Fact]
    public async Task Add_UnknownOption_NamesTheOption_AndIsNotTreatedAsAModuleName()
    {
        var (exit, _, stderr) = await RunAsync("add", "rendering", "--projekt", "x");

        Assert.Equal(2, exit);
        Assert.Contains("unknown option '--projekt'", stderr);
        Assert.Contains("`monodreams add`", stderr);
        // The old failure mode: the option token became a module name and the registry took the blame.
        Assert.DoesNotContain("Module '--projekt' not found", stderr);
    }

    [Fact]
    public async Task Add_UnknownOption_SuggestsTheClosestKnownOption()
    {
        var (exit, _, stderr) = await RunAsync("add", "rendering", "--dryrun");

        Assert.Equal(2, exit);
        Assert.Contains("Did you mean '--dry-run'?", stderr);
    }

    [Fact]
    public async Task List_UnknownOption_NamesTheOption_AndSuggests()
    {
        var (exit, _, stderr) = await RunAsync("list", "--verbos");

        Assert.Equal(2, exit);
        Assert.Contains("unknown option '--verbos'", stderr);
        Assert.Contains("Did you mean '--verbose'?", stderr);
    }

    [Fact]
    public async Task Init_UnknownOption_NamesTheOption()
    {
        var (exit, _, stderr) = await RunAsync("init", "MyGame", "--platfrom", "web");

        Assert.Equal(2, exit);
        Assert.Contains("unknown option '--platfrom'", stderr);
        Assert.Contains("Did you mean '--platform'?", stderr);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("migrate-colliders")]
    public async Task Migrate_UnknownOption_NamesTheOption_AndIsNotTreatedAsThePath(string command)
    {
        var (exit, _, stderr) = await RunAsync(command, "--bogus", "some/path");

        Assert.Equal(2, exit);
        Assert.Contains("unknown option '--bogus'", stderr);
        Assert.Contains($"`monodreams {command}`", stderr);
        // The old failure mode: '--bogus' became the path and the error complained about 'some/path'.
        Assert.DoesNotContain("some/path", stderr);
    }

    [Fact]
    public async Task KnownOption_WithAnotherOptionInItsValueSlot_IsRejected()
    {
        var (exit, _, stderr) = await RunAsync("add", "--preset", "--dry-run");

        Assert.Equal(2, exit);
        Assert.Contains("option '--preset'", stderr);
        Assert.Contains("expects a value", stderr);
    }

    [Fact]
    public async Task EndOfOptionsSeparator_KeepsDashedValuesAsPositionals()
    {
        // Everything after `--` is a literal: '--weird' must reach the command as the path, not be rejected.
        var (exit, _, stderr) = await RunAsync("migrate", "--", "--weird");

        Assert.Equal(2, exit); // the path does not exist — but the failure is about the PATH, not the parse
        Assert.DoesNotContain("unknown option", stderr);
        Assert.Contains("--weird", stderr);
    }

    [Fact]
    public async Task Help_IsAlwaysAccepted()
    {
        var (exit, stdout, _) = await RunAsync("add", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("--dir", stdout);
    }

    // ---- one canonical --dir, with --project as a hidden alias ---------------------------------

    [Fact]
    public async Task Add_Dir_SelectsTheProjectDirectory()
    {
        var (registryRoot, projectDir) = SetupSyntheticRegistryAndProject();
        try
        {
            var (exit, stdout, _) = await RunAsync(
                "add", "noop", "--dir", projectDir, "--dry-run", "--registry", registryRoot);

            Assert.Equal(0, exit);
            Assert.Contains($"project: {projectDir}", stdout);
        }
        finally { Cleanup(registryRoot, projectDir); }
    }

    [Fact]
    public async Task Add_ProjectAlias_StillSelectsTheProjectDirectory_WithADeprecationNote()
    {
        var (registryRoot, projectDir) = SetupSyntheticRegistryAndProject();
        try
        {
            var (exit, stdout, stderr) = await RunAsync(
                "add", "noop", "--project", projectDir, "--dry-run", "--registry", registryRoot);

            Assert.Equal(0, exit);
            Assert.Contains($"project: {projectDir}", stdout);
            Assert.Contains("--project is a deprecated alias for --dir", stderr);
        }
        finally { Cleanup(registryRoot, projectDir); }
    }

    [Fact]
    public async Task Add_DirAndProjectDisagreeing_IsAnError()
    {
        var (registryRoot, projectDir) = SetupSyntheticRegistryAndProject();
        try
        {
            var (exit, _, stderr) = await RunAsync(
                "add", "noop", "--dir", projectDir, "--project", Path.Combine(projectDir, "elsewhere"),
                "--dry-run", "--registry", registryRoot);

            Assert.Equal(2, exit);
            Assert.Contains("disagree", stderr);
        }
        finally { Cleanup(registryRoot, projectDir); }
    }

    [Fact]
    public async Task Init_AcceptsTheProjectAlias_ForTheSameConcept()
    {
        // An invalid project name stops `init` before it scaffolds anything — enough to prove the alias
        // parsed (an unknown option would have failed first, naming '--project').
        var (exit, _, stderr) = await RunAsync("init", "1bad", "--project", "somewhere");

        Assert.Equal(2, exit);
        Assert.DoesNotContain("unknown option", stderr);
        Assert.Contains("not a valid project name", stderr);
        Assert.Contains("--project is a deprecated alias for --dir", stderr);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("init")]
    public async Task DeprecatedAlias_IsHiddenFromHelp_SoOnlyOneSpellingIsDocumented(string command)
    {
        var (exit, stdout, _) = await RunAsync(command, "--help");

        Assert.Equal(0, exit);
        Assert.Contains("--dir", stdout);
        Assert.DoesNotContain("--project", stdout);
    }

    // ---- errors exit non-zero -------------------------------------------------------------------

    [Fact]
    public async Task ReportedError_ExitsNonZero()
    {
        var (registryRoot, projectDir) = SetupSyntheticRegistryAndProject();
        try
        {
            var (exit, _, stderr) = await RunAsync(
                "add", "no-such-module", "--dir", projectDir, "--dry-run", "--registry", registryRoot);

            Assert.Equal(2, exit);
            Assert.Contains("Module 'no-such-module' not found", stderr);
        }
        finally { Cleanup(registryRoot, projectDir); }
    }

    // ---- fixtures -------------------------------------------------------------------------------

    /// <summary>
    /// Drives the CLI exactly as the shell does and captures both streams. <see cref="Environment.ExitCode"/>
    /// is restored afterwards so a user-error run cannot leak into the test host's own exit code.
    /// </summary>
    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await Program.RunAsync(args);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Environment.ExitCode = 0;
        }
    }

    /// <summary>A registry holding one dependency-free module plus a project with a Core csproj — enough
    /// for a dry-run `add` to print its plan without touching the real engine registry.</summary>
    private static (string RegistryRoot, string ProjectDir) SetupSyntheticRegistryAndProject()
    {
        var registryRoot = CliTestSupport.NewTempDir("cliargs-reg");
        var moduleDir = Path.Combine(registryRoot, "MonoDreams", "noop");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "module.json"),
            """{ "name": "noop", "description": "no-op module", "platforms": ["desktop"] }""");

        var projectDir = CliTestSupport.NewTempDir("cliargs-proj");
        var coreDir = Path.Combine(projectDir, "Proj.Core");
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(coreDir, "Proj.Core.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");

        var state = StateFile.LoadOrCreate(projectDir);
        state.Platforms = new List<string> { "desktop" };
        state.Save(projectDir);

        return (registryRoot, projectDir);
    }

    private static void Cleanup(string registryRoot, string projectDir)
    {
        TryDelete(registryRoot);
        TryDelete(projectDir);
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
