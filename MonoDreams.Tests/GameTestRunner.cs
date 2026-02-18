using System.Diagnostics;
using System.Text.Json;
using MonoDreams.Input;
using MonoDreams.State;

namespace MonoDreams.Tests;

public class GameTestResult
{
    public int ExitCode { get; init; }
    public List<string> LogLines { get; init; } = new();

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
}

public static class GameTestRunner
{
    /// <summary>
    /// Finds the repo root by walking up from the test assembly's base directory
    /// looking for the MonoDreams.Examples directory.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "MonoDreams.Examples")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (directory containing MonoDreams.Examples).");
    }

    public static async Task<GameTestResult> RunAsync(InputReplayPlan plan, int timeoutSeconds = 30)
    {
        var repoRoot = FindRepoRoot();
        var debugDir = Path.Combine(Path.GetTempPath(), "monodreams_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(debugDir);

        // Write the replay plan
        var replayJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(debugDir, "input_replay.json"), replayJson);

        // Spawn the game process
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project MonoDreams.Examples -- --headless",
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

        // Read log files from the debug dir
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
        };
    }
}
