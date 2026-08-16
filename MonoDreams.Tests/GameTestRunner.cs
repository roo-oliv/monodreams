using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MonoDreams.Debug.Input;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;

namespace MonoDreams.Tests;

public class GameTestResult
{
    public int ExitCode { get; init; }
    public List<string> LogLines { get; init; } = new();

    /// The spawned process's captured stdout / stderr — a crashing game prints its unhandled
    /// exception to stderr, which the debug log never sees. Surfaced by
    /// <see cref="AssertExitedCleanly"/> so a nonzero exit is diagnosable from the test output
    /// alone (a CI runner's temp debug dir is gone by the time anyone looks).
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";

    /// The temp debug directory the run wrote its log + screenshots into.
    public string DebugDir { get; init; } = "";

    /// The isolated temp project root the run was pinned to via <c>MONODREAMS_PROJECT_ROOT</c> (see
    /// <see cref="GameTestRunner"/>). A resolved editor process writes only under here, never the real
    /// repo content tree — assert against it to prove isolation held.
    public string ProjectRoot { get; init; } = "";

    /// <summary>Asserts the game exited 0 — and on failure reports WHY: the stderr (the unhandled
    /// exception lives there) plus the debug log's tail, instead of a bare "Expected: 0".</summary>
    public void AssertExitedCleanly()
    {
        if (ExitCode == 0) return;
        var logTail = string.Join("\n", LogLines.TakeLast(15));
        Assert.Fail(
            $"Game process exited {ExitCode} (expected 0).\n" +
            $"--- stderr ---\n{StdErr.Trim()}\n" +
            $"--- debug log tail ---\n{logTail}");
    }

    public void AssertLogContains(string substring)
    {
        Assert.Contains(LogLines, line => line.Contains(substring, StringComparison.OrdinalIgnoreCase));
    }

    public void AssertLogContainsInOrder(params string[] substrings)
    {
        int searchFrom = 0;
        foreach (var substring in substrings)
        {
            int found = -1;
            for (int i = searchFrom; i < LogLines.Count; i++)
            {
                if (LogLines[i].Contains(substring, StringComparison.OrdinalIgnoreCase))
                {
                    found = i;
                    break;
                }
            }

            Assert.True(found >= 0, $"Expected log to contain '{substring}' after line {searchFrom}, but it was not found.");
            searchFrom = found + 1;
        }
    }

    public List<string> GetLogLines(LogLevel level)
    {
        var tag = level switch
        {
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Info => "[ INFO]",
            LogLevel.Warning => "[ WARN]",
            LogLevel.Error => "[ERROR]",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
        return LogLines.Where(l => l.Contains(tag)).ToList();
    }

    // ─── headless-Demos assertions (issue #28) ───────────────────────────────

    /// Asserts the run dropped at least one PNG into the debug dir. Proof that the
    /// headless Draw path produced frames (the Examples headless mode produced none).
    public void AssertScreenshotExists()
    {
        var pngs = Directory.GetFiles(DebugDir, "*.png");
        Assert.True(pngs.Length > 0, $"Expected at least one screenshot PNG in '{DebugDir}', found none.");
    }

    /// Asserts at least one captured frame was non-blank (more than one distinct
    /// colour). The headless capture logs a "nonBlank=True/False" metric per frame;
    /// this checks the metric so the test needs no GraphicsDevice to decode the PNG.
    public void AssertScreenshotNonBlank()
    {
        AssertScreenshotExists();
        Assert.Contains(LogLines, line => line.Contains("nonBlank=True", StringComparison.OrdinalIgnoreCase));
    }

    /// Parses the periodic "Heap sample: ... bytes=N" log lines and asserts the live
    /// managed heap stays flat across the run — i.e. a retained-object leak (like the
    /// per-frame EntitySet leak from #27) is absent. The first <paramref name="skipSamples"/>
    /// samples are dropped as JIT/content warmup; the remainder must stay within
    /// <paramref name="maxGrowthRatio"/> of the smallest post-warmup sample.
    public void AssertHeapFlat(double maxGrowthRatio = 1.5, int skipSamples = 1)
    {
        var samples = GetHeapSamples();
        Assert.True(samples.Count > skipSamples + 1,
            $"Expected more than {skipSamples + 1} heap samples, found {samples.Count}.");

        var stable = samples.Skip(skipSamples).ToList();
        var min = stable.Min();
        var max = stable.Max();
        Assert.True(min > 0, "Heap samples must be positive.");
        Assert.True(max <= min * maxGrowthRatio,
            $"Live heap grew from {min} to {max} bytes across the run (ratio {(double)max / min:F2} > {maxGrowthRatio}). " +
            "This looks like a leak — a static scene should hold a flat live heap.");
    }

    private static readonly Regex HeapSampleRegex = new(@"Heap sample:.*bytes=(\d+)", RegexOptions.Compiled);

    public List<long> GetHeapSamples()
    {
        var result = new List<long>();
        foreach (var line in LogLines)
        {
            var m = HeapSampleRegex.Match(line);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var bytes))
                result.Add(bytes);
        }
        return result;
    }
}

public static class GameTestRunner
{
    /// <summary>
    /// Finds the repo root by walking up from the test assembly's base directory
    /// looking for the MonoDreams.Examples.Desktop head project directory.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "MonoDreams.Examples.Desktop")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (directory containing MonoDreams.Examples.Desktop).");
    }

    /// <summary>
    /// Runs the Examples host headless under an input-replay plan and collects its log.
    /// <paramref name="environment"/> adds extra process environment variables (e.g.
    /// <c>MONODREAMS_EDITOR=1</c> for editor-flag runs). <paramref name="editorOpPlan"/> drops an
    /// <c>editor_op_plan.json</c> into the debug dir — the headless editor-op channel; useful on
    /// screens without an <c>InputReplaySystem</c> (the menu), where the op driver owns the exit.
    /// <paramref name="pointerPlan"/> drops a <c>pointer_replay.json</c> — the scripted-mouse channel
    /// (issue #90), which is how a mouse-first screen (the menu) gets driven at all: the input replay
    /// speaks only named actions.
    /// </summary>
    public static async Task<GameTestResult> RunAsync(InputReplayPlan plan, int timeoutSeconds = 30,
        IReadOnlyDictionary<string, string>? environment = null, EditorOpPlan? editorOpPlan = null,
        PointerReplayPlan? pointerPlan = null)
    {
        var debugDir = CreateDebugDir();

        var replayJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(debugDir, "input_replay.json"), replayJson);

        if (editorOpPlan != null)
        {
            var opJson = JsonSerializer.Serialize(editorOpPlan, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(debugDir, "editor_op_plan.json"), opJson);
        }

        if (pointerPlan != null) await WritePointerPlanAsync(debugDir, pointerPlan);

        return await RunProcessAsync("MonoDreams.Examples.Desktop", "--headless", debugDir,
            timeoutSeconds, environment);
    }

    /// <summary>Serializes a pointer plan into <c>pointer_replay.json</c> in the run's debug dir.
    /// Enum members go out as NAMES (the plan's own JSON contract), not integers.</summary>
    private static Task WritePointerPlanAsync(string debugDir, PointerReplayPlan pointerPlan)
    {
        var json = JsonSerializer.Serialize(pointerPlan, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        });
        return File.WriteAllTextAsync(Path.Combine(debugDir, PointerReplayPlan.FileName), json);
    }

    /// <summary>
    /// Runs the Demos host headless on a single demo screen for a fixed number of
    /// frames, then collects its log + screenshots. Mirrors <see cref="RunAsync"/>
    /// but targets the observe-and-self-verify path from issue #28.
    /// <paramref name="environment"/> adds extra process environment variables (e.g.
    /// <c>MONODREAMS_EDITOR=1</c> for editor-flag runs). Unless the caller sets it,
    /// <c>MONODREAMS_EDITOR</c> is pinned off so a developer's exported flag can never
    /// perturb the flag-off headless contract these runs assert.
    /// </summary>
    public static async Task<GameTestResult> RunDemosAsync(
        string screen,
        int frames,
        int captureEvery = 0,
        int sampleEvery = 30,
        int timeoutSeconds = 120,
        IReadOnlyDictionary<string, string>? environment = null,
        EditorOpPlan? editorOpPlan = null)
    {
        var debugDir = CreateDebugDir();
        var args = $"--headless --screen {screen} --frames {frames} " +
                   $"--exit --capture-every {captureEvery} --sample-every {sampleEvery}";

        // The headless editor-op channel (TD): the Demos launcher has no InputReplaySystem, so a scripted
        // op (e.g. a cross-screen tab:open) is the way to drive it — the destination screen's op driver
        // owns the exit, exactly as RunAsync does for the Examples menu.
        if (editorOpPlan != null)
        {
            var opJson = JsonSerializer.Serialize(editorOpPlan, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(debugDir, "editor_op_plan.json"), opJson);
        }

        var env = new Dictionary<string, string> { ["MONODREAMS_EDITOR"] = "0" };
        if (environment != null)
            foreach (var (key, value) in environment)
                env[key] = value;

        return await RunProcessAsync("MonoDreams.Demos", args, debugDir, timeoutSeconds, env);
    }

    private static string CreateDebugDir()
    {
        var debugDir = Path.Combine(Path.GetTempPath(), "monodreams_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(debugDir);
        return debugDir;
    }

    /// <summary>
    /// Creates a throwaway, per-run editor <b>project root</b> and pins the spawned process to it via
    /// <c>MONODREAMS_PROJECT_ROOT</c> (see <see cref="RunProcessAsync"/>). This is the safe-by-construction
    /// isolation guarantee: a spawned editor process (or a process the developer's ambient
    /// <c>MONODREAMS_EDITOR=1</c> turned into one) resolves THIS temp tree — never the real repo
    /// <c>MonoDreams.Examples.Core/Content</c> — so no test can ever write the user's real
    /// <c>Content.mgcb</c> / <c>Levels</c> / <c>Prefabs</c>.
    ///
    /// <para><b>The manifest is mandatory.</b> <see cref="EditorProjectContext"/>'s env-var branch, when it
    /// finds no <c>game.mdproj</c> at the named root, <b>falls through</b> to the walk-up + repo-root search
    /// and re-discovers the REAL source manifest. So this writes a minimal
    /// <c>&lt;root&gt;/Content/game.mdproj</c> (resolving <c>ProjectRoot</c> to <c>&lt;root&gt;/Content</c>,
    /// mirroring the real layout) plus the <c>Levels</c>/<c>Prefabs</c> dirs and an empty
    /// <c>Content.mgcb</c> so any Save / zero-touch bundle lands in the isolated tree.</para>
    /// </summary>
    private static string CreateIsolatedProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "monodreams_proj_" + Guid.NewGuid().ToString("N")[..8]);
        var content = Path.Combine(root, "Content");
        Directory.CreateDirectory(Path.Combine(content, MgcbLevelBundle.LevelsDirectoryName));
        Directory.CreateDirectory(Path.Combine(content, MgcbLevelBundle.PrefabsDirectoryName));
        File.WriteAllText(Path.Combine(content, GameProject.FileName),
            "{\n  \"formatVersion\": 1,\n  \"startScene\": \"\",\n  \"levelsDir\": \"Levels\"\n}\n");
        File.WriteAllText(Path.Combine(content, MgcbLevelBundle.McgbFileName), "");
        return root;
    }

    /// <summary>One Release build per HEAD per test process, serialized — never at spawn time. A
    /// spawn-time `dotnet run` build on a cold machine blows the per-test timeout (full build + the
    /// MGCB dotnet-tool install), and a timeout kill mid-tool-install poisons the NuGet tool store
    /// for every later build ("Cannot create … already exists" — the CI failure this retired).
    /// Core builds FIRST, its own step: the heads' MGCB content step references MonoDreams.dll by
    /// absolute path, not as an MSBuild dependency (the repo's core-first build rule).</summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task>> BuiltHeads = new();

    private static Task EnsureBuiltAsync(string project) =>
        BuiltHeads.GetOrAdd(project, p => new Lazy<Task>(() => Task.Run(async () =>
        {
            foreach (var target in new[] { Path.Combine("MonoDreams", "MonoDreams.csproj"), p })
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build {target} --configuration Release --nologo -v q",
                    WorkingDirectory = FindRepoRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
                using var build = Process.Start(psi)!;
                var stdOut = build.StandardOutput.ReadToEndAsync();
                var stdErr = build.StandardError.ReadToEndAsync();
                await build.WaitForExitAsync();
                if (build.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"Pre-building '{target}' (Release) failed with exit {build.ExitCode}.\n" +
                        $"{await stdOut}\n{await stdErr}");
            }
        }))).Value;

    private static async Task<GameTestResult> RunProcessAsync(string project, string gameArguments,
        string debugDir, int timeoutSeconds, IReadOnlyDictionary<string, string>? environment = null)
    {
        var repoRoot = FindRepoRoot();
        var projectRoot = CreateIsolatedProjectRoot();
        await EnsureBuiltAsync(project);
        // --no-build: the spawn only evaluates the project to find the Release output — no compile,
        // no content build, no tool install — so a spawn is fast and safe on any machine state.
        var arguments = $"run --configuration Release --no-build --project {project} -- {gameArguments}";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["MONODREAMS_DEBUG_DIR"] = debugDir;
        // `dotnet run` spawns persistent MSBuild worker nodes that inherit the redirected pipe
        // handles; with node reuse they outlive the game by ~15 minutes and keep the pipes open
        // (the CI hang). No daemons — each run is hermetic.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // macOS: never steal the user's focus at launch (the heads set this themselves under
        // --headless; the env covers any spawn path). SDL hint, ignored elsewhere.
        psi.Environment["SDL_MAC_BACKGROUND_APP"] = "1";
        // Pin the editor project root to the isolated temp tree BEFORE the caller's env, so it applies to
        // every spawned head (editor-on or ambiently editor-on) yet an explicit caller override still wins.
        psi.Environment[EditorProjectContext.ProjectRootVariable] = projectRoot;
        if (environment != null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        using var process = Process.Start(psi)!;
        // Drain both pipes from the start: an unread redirected pipe fills up and DEADLOCKS a
        // chatty child, and stderr is where a crashing game prints its unhandled exception.
        // Event-based (not ReadToEnd) on purpose — ReadToEnd completes only when EVERY handle
        // holder closes the pipe, and `dotnet run` grandchildren (MSBuild nodes) can hold it
        // long after the game exits; events deliver what arrived without waiting for pipe EOF.
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var stdOutEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdErrEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) stdOutEof.TrySetResult();
            else lock (stdOut) stdOut.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) stdErrEof.TrySetResult();
            else lock (stdErr) stdErr.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Game process did not exit within {timeoutSeconds}s.");
        }

        // Wait for both streams' EOF signals (e.Data == null) so a large final line is never lost —
        // but BOUNDED: `dotnet run` grandchildren (MSBuild nodes) can hold the pipe handles open
        // long after the game exits, and EOF then never comes; what arrived is already captured.
        try { await Task.WhenAll(stdOutEof.Task, stdErrEof.Task).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { /* descendant still holds the pipes — proceed with what we have */ }

        var logLines = new List<string>();
        var logFiles = Directory.GetFiles(debugDir, "monodreams_*.log");
        foreach (var logFile in logFiles.OrderBy(f => f))
        {
            logLines.AddRange(await File.ReadAllLinesAsync(logFile));
        }

        string stdOutText, stdErrText;
        lock (stdOut) stdOutText = stdOut.ToString();
        lock (stdErr) stdErrText = stdErr.ToString();
        return new GameTestResult
        {
            ExitCode = process.ExitCode,
            LogLines = logLines,
            StdOut = stdOutText,
            StdErr = stdErrText,
            DebugDir = debugDir,
            ProjectRoot = projectRoot,
        };
    }
}
