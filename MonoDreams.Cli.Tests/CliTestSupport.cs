using System.Diagnostics;

namespace MonoDreams.Cli.Tests;

internal static class CliTestSupport
{
    /// <summary>
    /// Walks up from the test assembly to the repo root (the directory containing
    /// <c>MonoDreams/module.schema.json</c>) — the registry path the CLI reads engine manifests + source from.
    /// </summary>
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "MonoDreams", "module.schema.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (directory containing MonoDreams/module.schema.json).");
    }

    public static string NewTempDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"md-{tag}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Deletes the temp work dir a scaffolded project lives in (<paramref name="projectDir"/>'s parent).
    /// Set <c>MD_KEEP_TEMP=1</c> to keep it for diagnostics.
    /// </summary>
    public static void TryDeleteWorkDir(string projectDir)
    {
        if (Environment.GetEnvironmentVariable("MD_KEEP_TEMP") == "1") return; // diagnostics escape hatch
        var work = Directory.GetParent(projectDir)?.FullName;
        try { if (work != null && Directory.Exists(work)) Directory.Delete(work, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Builds a scaffolded project (a <c>.sln</c> or a <c>.csproj</c>) the way a user's shell would, and
    /// returns the exit code plus the combined stdout/stderr. Shared by every test that proves generated
    /// projects compile (<see cref="ScaffolderBuildTests"/>, <see cref="ManifestHonestyTests"/>) so the
    /// hard-won incantation below lives in exactly one place.
    ///
    /// The build is launched through <c>env -i</c> (Unix only — see <see cref="CanBuildScaffoldedProjects"/>):
    /// VSTest's testhost injects MSBuild/SDK build-context vars (MSBuildSDKsPath, MSBuildExtensionsPath, …)
    /// and MSBuild server / assembly-resolver state pinned to the SDK running the tests. A `dotnet build`
    /// spawned in-process inherits enough of that context that a BlazorWebAssembly head's Razor source
    /// generator mis-resolves and the .razor component base silently fails to compile — though the identical
    /// build succeeds from a developer shell. A genuinely pristine environment matches the shell build.
    ///
    /// Flags: <c>-m:1</c> + <c>/nodeReuse:false</c> — single node, no persistent MSBuild server (the
    /// persistent node keeps its stdout/stderr pipes open after the build logically finishes, deadlocking a
    /// synchronous read; <c>-m:1</c> also avoids the obj-lock race on parallel web builds).
    /// <c>-p:UseSharedCompilation=false</c> — do not connect to a shared Roslyn build server whose Razor
    /// source-generator state belongs to the test host's build context.
    /// <c>-p:RestoreDisableParallel=true</c> — a .sln restores Core once as a member and once as the Desktop
    /// head's ProjectReference; NuGet's parallel restore races both writes of Core/obj/project.nuget.cache
    /// ("the file … already exists" → exit 1), a flake independent of <c>-m:1</c> (which serialises the
    /// build, not restore).
    /// <c>DOTNET_CLI_UI_LANGUAGE=en</c> — compiler diagnostics in English regardless of the developer's
    /// locale, so callers can match on diagnostic text (the manifest-honesty known-gap markers do).
    /// </summary>
    public static (int ExitCode, string Output) BuildScaffoldedProject(
        string projectOrSln, string? platformArg = null, int timeoutMinutes = 8)
    {
        CleanBuildOutputs(projectOrSln);

        var args = $"build \"{projectOrSln}\" -c Debug --nologo -m:1 /nodeReuse:false " +
                   "-p:UseSharedCompilation=false -p:RestoreDisableParallel=true";
        if (platformArg is not null) args += $" -p:MonoDreamsPlatform={platformArg}";

        var keep = new (string Key, string? Val)[]
        {
            ("PATH", Environment.GetEnvironmentVariable("PATH")),
            ("HOME", Environment.GetEnvironmentVariable("HOME")),
            ("DOTNET_ROOT", Environment.GetEnvironmentVariable("DOTNET_ROOT")),
            ("TMPDIR", Environment.GetEnvironmentVariable("TMPDIR")),
            ("LANG", Environment.GetEnvironmentVariable("LANG")),
        };
        var envPrefix = string.Join(" ", keep.Where(k => k.Val is not null).Select(k => $"{k.Key}=\"{k.Val}\""))
                        + " MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_UI_LANGUAGE=en";

        var psi = new ProcessStartInfo("/usr/bin/env", $"-i {envPrefix} dotnet {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Build from the project's own folder (outside the repo) so the MSBuild directory walk-up
            // matches a developer building the scaffolded project, not the test host's CWD inside the repo.
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectOrSln))!,
        };

        using var proc = Process.Start(psi)!;
        // Drain both pipes asynchronously so a full pipe buffer on one stream can never block the other.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(milliseconds: timeoutMinutes * 60 * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (-1, $"dotnet build did not finish within {timeoutMinutes} minutes for {projectOrSln}");
        }

        var output = stdoutTask.GetAwaiter().GetResult() + Environment.NewLine + stderrTask.GetAwaiter().GetResult();
        return (proc.ExitCode, output);
    }

    /// <summary>
    /// True when <see cref="BuildScaffoldedProject"/> can run here. The pristine-environment launcher is
    /// <c>/usr/bin/env -i</c>, which Windows has no equivalent for; the cross-platform proof runs on the
    /// Unix hosts (developer machines + CI).
    /// </summary>
    public static bool CanBuildScaffoldedProjects() => !OperatingSystem.IsWindows();

    /// <summary>Last <paramref name="n"/> characters of <paramref name="s"/> — build logs are long.</summary>
    public static string Tail(string s, int n) => s.Length <= n ? s : s.Substring(s.Length - n);

    /// <summary>
    /// Writes a failed build's full output next to the OS temp dir and returns the path, so a CI failure
    /// carries more than the tail the assertion message can hold.
    /// </summary>
    public static string DumpBuildLog(string tag, string args, string output)
    {
        var log = Path.Combine(Path.GetTempPath(), $"md-build-fail-{tag}.log");
        try { File.WriteAllText(log, $"TARGET: {args}\n\n{output}"); } catch { /* best effort */ }
        return log;
    }

    /// <summary>
    /// Wipes every obj/bin under the project tree before building. The shared Core library builds once per
    /// backend; a desktop-built Core obj left from an earlier build (e.g. a .sln build, or a transitive
    /// restore) is reused by a subsequent web-head build and the KNI/Razor compilation resolves against the
    /// wrong backend (surfacing as a spurious Razor base-class error). A from-scratch user build has no such
    /// obj; cleaning makes the test match that.
    /// </summary>
    private static void CleanBuildOutputs(string projectOrSln)
    {
        var projectRoot = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(projectOrSln))!)!.FullName;
        foreach (var d in Directory.EnumerateDirectories(projectRoot, "obj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateDirectories(projectRoot, "bin", SearchOption.AllDirectories))
                     .ToList())
        {
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        }
    }
}
