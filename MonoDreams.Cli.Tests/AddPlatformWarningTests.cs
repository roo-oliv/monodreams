using MonoDreams.Cli.Commands;
using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Phase 4b contract protection for <c>monodreams add</c>'s platform awareness: a multi-platform project
/// (desktop + web) that requests a module supported on only one of its platforms gets a warning and the
/// module is skipped for the unsupported platform (still installed for the supported one); a module
/// supported on none of the project's platforms is a hard error. Drives <see cref="Runner.RunAddAsync"/>
/// against a synthetic temp registry, in dry-run (no file copies) so the test is fast and self-contained.
/// </summary>
public class AddPlatformWarningTests
{
    [Fact]
    public async Task Add_MultiProject_WarnsAndSkipsDesktopOnlyModuleForWeb_StillInstallsForDesktop()
    {
        var (registryRoot, projectDir) = SetupSyntheticRegistryAndProject(projectPlatforms: new[] { "desktop", "web" });
        try
        {
            var (stdout, stderr) = await CaptureAsync(() =>
                Runner.RunAddAsync(new[] { "deskonly" }, presetName: null, projectPath: projectDir, dryRun: true, registryPath: registryRoot));

            // Warns that the desktop-only module is skipped for web, but it is still in the plan (desktop).
            Assert.Contains("does not support platform 'web'", stderr);
            Assert.Contains("to install: deskonly", stdout);
        }
        finally { Cleanup(registryRoot, projectDir); }
    }

    [Fact]
    public async Task Add_WebOnlyProject_RejectsDesktopOnlyModule_Hard()
    {
        var (registryRoot, projectDir) = SetupSyntheticRegistryAndProject(projectPlatforms: new[] { "web" });
        try
        {
            Environment.ExitCode = 0;
            var (_, stderr) = await CaptureAsync(() =>
                Runner.RunAddAsync(new[] { "deskonly" }, presetName: null, projectPath: projectDir, dryRun: true, registryPath: registryRoot));

            // The module supports none of the project's platforms (web-only project, desktop-only module).
            Assert.Contains("support none of this project's target platform", stderr);
            Assert.Equal(2, Environment.ExitCode);
        }
        finally { Environment.ExitCode = 0; Cleanup(registryRoot, projectDir); }
    }

    // ---- fixtures -----------------------------------------------------------------------------

    private static (string RegistryRoot, string ProjectDir) SetupSyntheticRegistryAndProject(string[] projectPlatforms)
    {
        var registryRoot = CliTestSupport.NewTempDir("addwarn-reg");
        var modulesDir = Path.Combine(registryRoot, "MonoDreams");
        Directory.CreateDirectory(modulesDir);

        // A desktop-only module with no source files (dry-run never copies; the file enumeration is empty).
        var deskDir = Path.Combine(modulesDir, "deskonly");
        Directory.CreateDirectory(deskDir);
        File.WriteAllText(Path.Combine(deskDir, "module.json"),
            """{ "name": "deskonly", "description": "desktop only", "platforms": ["desktop"] }""");

        // A project with a recorded target platform set and a bare Core csproj for the editor to touch.
        var projectDir = CliTestSupport.NewTempDir("addwarn-proj");
        var coreDir = Path.Combine(projectDir, "Proj.Core");
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(coreDir, "Proj.Core.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");

        var state = StateFile.LoadOrCreate(projectDir);
        state.Platforms = projectPlatforms.ToList();
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

    private static async Task<(string Stdout, string Stderr)> CaptureAsync(Func<Task> action)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var outW = new StringWriter();
        var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        try { await action(); }
        finally { Console.SetOut(origOut); Console.SetError(origErr); }
        return (outW.ToString(), errW.ToString());
    }
}
