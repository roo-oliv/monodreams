using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MonoDreams.Input;
using MonoDreams.State;

namespace MonoDreams.Tests;

public class GameTestResult
{
    public int ExitCode { get; init; }
    public List<string> LogLines { get; init; } = new();

    /// The temp debug directory the run wrote its log + screenshots into.
    public string DebugDir { get; init; } = "";

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
    /// </summary>
    public static async Task<GameTestResult> RunAsync(InputReplayPlan plan, int timeoutSeconds = 30)
    {
        var debugDir = CreateDebugDir();

        var replayJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(debugDir, "input_replay.json"), replayJson);

        return await RunProcessAsync("run --project MonoDreams.Examples.Desktop -- --headless", debugDir, timeoutSeconds);
    }

    /// <summary>
    /// Runs the Demos host headless on a single demo screen for a fixed number of
    /// frames, then collects its log + screenshots. Mirrors <see cref="RunAsync"/>
    /// but targets the observe-and-self-verify path from issue #28.
    /// </summary>
    public static Task<GameTestResult> RunDemosAsync(
        string screen,
        int frames,
        int captureEvery = 0,
        int sampleEvery = 30,
        int timeoutSeconds = 120)
    {
        var debugDir = CreateDebugDir();
        var args = $"run --project MonoDreams.Demos -- --headless --screen {screen} --frames {frames} " +
                   $"--exit --capture-every {captureEvery} --sample-every {sampleEvery}";
        return RunProcessAsync(args, debugDir, timeoutSeconds);
    }

    private static string CreateDebugDir()
    {
        var debugDir = Path.Combine(Path.GetTempPath(), "monodreams_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(debugDir);
        return debugDir;
    }

    private static async Task<GameTestResult> RunProcessAsync(string arguments, string debugDir, int timeoutSeconds)
    {
        var repoRoot = FindRepoRoot();

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

        using var process = Process.Start(psi)!;
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

        var logLines = new List<string>();
        var logFiles = Directory.GetFiles(debugDir, "monodreams_*.log");
        foreach (var logFile in logFiles.OrderBy(f => f))
        {
            logLines.AddRange(await File.ReadAllLinesAsync(logFile));
        }

        return new GameTestResult
        {
            ExitCode = process.ExitCode,
            LogLines = logLines,
            DebugDir = debugDir,
        };
    }
}
