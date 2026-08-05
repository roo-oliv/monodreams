using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MonoDreams.Platform;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the <see cref="Logger"/> interpolated-string-handler seam (issue #37): a log line at a
/// suppressed level must cost nothing — the interpolation holes are never evaluated — while an
/// enabled line stays byte-identical to what the plain <c>string</c> overload has always produced.
/// Both halves matter: the first is the whole point of the handler, the second is why nobody has to
/// audit 300+ call sites after it lands.
///
/// The tests drive a fake <see cref="IPlatformServices"/> so both sinks (the log writer and the
/// console echo) are observable with no real disk. <see cref="Logger"/> and
/// <see cref="PlatformServices.Current"/> are process-global static state, so this class shares the
/// non-parallel collection with <c>PlatformServicesTests</c>, restores the desktop default in a
/// finally, and resets the logger's threshold on both sides of every test (see the ctor/Dispose
/// pair below).
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class LoggerInterpolationTests : IDisposable
{
    // xUnit builds one instance per test: the ctor runs before it and Dispose after it, so the
    // process-global Logger is returned to its default threshold on BOTH sides. That matters more
    // than usual now — `Shutdown` does not reset `MinimumLevel`, and leaving it at Warning/Error
    // would silently suppress (and, at an interpolated call site, skip the holes of) every later
    // test's log lines in this assembly.
    public LoggerInterpolationTests() => ResetLoggerStatics();

    public void Dispose() => ResetLoggerStatics();

    /// <summary>The format contract every emitted line must satisfy — the input-replay /
    /// verification workflow and the tooling greps parse it, so it is pinned here rather than
    /// left to reviewer memory.</summary>
    private const string FormatContract =
        @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[GT (  N/A |[ \d]+\.\d{2})\] \[(DEBUG| INFO| WARN|ERROR)\] ";

    /// <summary>
    /// Minimal in-memory <see cref="IPlatformServices"/>: enough to capture the two Logger sinks.
    /// Mirrors the fake in <c>PlatformServicesTests</c>.
    /// </summary>
    private sealed class FakePlatformServices : IPlatformServices
    {
        public string BaseDirectory => "/fake/base/";
        public List<string> ConsoleLines { get; } = new();
        public List<string> CreatedDirectories { get; } = new();
        public StringWriter LogWriter { get; } = new();

        // IPlatformServices is nullable-oblivious (the engine does not enable NRT); `null!` keeps
        // this nullable-enabled test assembly quiet without changing the contract.
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
        public void CreateDirectory(string path) => CreatedDirectories.Add(path);

        public TextWriter OpenLogWriter(string directory, string fileName)
        {
            CreatedDirectories.Add(directory);
            return LogWriter;
        }

        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
        public void RunBackground(Action work) => work();
    }

    /// <summary>A hole the handler must never touch at a suppressed level: touching it throws.</summary>
    private sealed class Boom
    {
        public override string ToString() =>
            throw new InvalidOperationException("interpolation hole was evaluated at a suppressed level");
    }

    private static void RunWithFake(FakePlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            body();
        }
        finally
        {
            PlatformServices.Current = previous; // restore desktop default
        }
    }

    /// <summary>
    /// <see cref="Logger"/> is process-global: <see cref="Logger.Initialize"/> is a no-op while an
    /// earlier session is still open, only <see cref="Logger.Shutdown"/> resets the game-time field
    /// behind <c>[GT ...]</c>, and nothing but a fresh <c>Initialize</c> resets
    /// <see cref="Logger.MinimumLevel"/>. An open-then-close pair on a throwaway sink therefore
    /// restores all three — closed, <c>[GT   N/A ]</c>, threshold back at <see cref="LogLevel.Debug"/>
    /// — whatever ran before. The sink is throwaway because <see cref="StringWriter"/> refuses
    /// writes once Shutdown disposed it.
    /// </summary>
    private static void ResetLoggerStatics()
    {
        RunWithFake(new FakePlatformServices(), () =>
        {
            Logger.Shutdown();
            Logger.Initialize("scratch");
            Logger.Shutdown();
        });
    }

    /// <summary>Every line the two sinks saw, minus the Logger's own start/stop bookkeeping.</summary>
    private static List<string> EmittedLines(FakePlatformServices fake) =>
        fake.ConsoleLines
            .Where(l => !l.Contains("Logger initialized.") && !l.Contains("Logger shutting down."))
            .ToList();

    private static string WithoutWallClock(string line) => Regex.Replace(line, @"^\[[^\]]+\] ", "");

    // ─── (a) a suppressed level evaluates nothing ─────────────────────────────────────────────────

    [Fact]
    public void SuppressedLevel_NeverEvaluatesInterpolationHoles_AndWritesNoLine()
    {
        var fake = new FakePlatformServices();
        RunWithFake(fake, () =>
        {
            try
            {
                Logger.Initialize("logdir", LogLevel.Info);
                Assert.Equal(LogLevel.Info, Logger.MinimumLevel);
                Assert.False(Logger.IsEnabled(LogLevel.Debug));
                Assert.True(Logger.IsEnabled(LogLevel.Info));

                // Binds to Logger.Debug(ref Message<AtDebug>). The handler's ctor reports
                // shouldAppend:false, so the compiler skips the hole entirely — Boom.ToString()
                // is never called. If this ever binds to the plain string overload instead, the
                // interpolation runs eagerly and this line throws.
                Logger.Debug($"never built {new Boom()}");

                Logger.Shutdown(); // flush
            }
            finally
            {
                Logger.Shutdown();
            }

            Assert.DoesNotContain(fake.ConsoleLines, l => l.Contains("[DEBUG]"));
            Assert.DoesNotContain(fake.ConsoleLines, l => l.Contains("never built"));
            Assert.DoesNotContain("[DEBUG]", fake.LogWriter.ToString());
            Assert.DoesNotContain("never built", fake.LogWriter.ToString());
        });
    }

    [Fact]
    public void SuppressedLevel_HoleIsSkipped_ForEveryLevelBelowTheThreshold()
    {
        var fake = new FakePlatformServices();
        RunWithFake(fake, () =>
        {
            try
            {
                Logger.Initialize("logdir", LogLevel.Error);

                // Three levels below the threshold, three handler instantiations, none of which
                // may touch its hole.
                Logger.Debug($"debug {new Boom()}");
                Logger.Info($"info {new Boom()}");
                Logger.Warning($"warning {new Boom()}");

                // ...and the one at the threshold still builds and emits.
                Logger.Error($"error {40 + 2}");

                Logger.Shutdown();
            }
            finally
            {
                Logger.Shutdown();
            }

            var lines = EmittedLines(fake);
            var line = Assert.Single(lines);
            Assert.EndsWith("[ERROR] error 42", line);
        });
    }

    // ─── (b) an enabled line is byte-identical between the two call forms ─────────────────────────

    [Fact]
    public void EnabledLevel_HandlerAndStringOverloads_EmitIdenticalLines()
    {
        var fake = new FakePlatformServices();
        RunWithFake(fake, () =>
        {
            const string name = "orb";
            const float x = 3.14159f;

            try
            {
                Logger.Initialize("logdir", LogLevel.Debug);

                // Form 1: an interpolated string literal — binds to Logger.Info(ref Message<AtInfo>).
                // The alignment/format specifier is the interesting part: the handler delegates to
                // DefaultInterpolatedStringHandler precisely so `{x,6:F2}` behaves identically.
                Logger.Info($"marker entity {name} at {x,6:F2} done");

                // Form 2: a string variable — no interpolation left to defer, so it binds to the
                // plain Logger.Info(string) overload, exactly as every pre-#37 call site did.
                var prebuilt = $"marker entity {name} at {x,6:F2} done";
                Logger.Info(prebuilt);

                Logger.Shutdown();
            }
            finally
            {
                Logger.Shutdown();
            }

            var lines = EmittedLines(fake).Where(l => l.Contains("marker entity")).ToList();
            Assert.Equal(2, lines.Count);

            // The wall clock legitimately differs between the two writes; everything after it —
            // the [GT ...] field, the level tag and the message itself — must be identical.
            Assert.Equal(WithoutWallClock(lines[0]), WithoutWallClock(lines[1]));

            foreach (var line in lines)
            {
                Assert.Matches(FormatContract, line);
                Assert.EndsWith($"[ INFO] marker entity orb at {x,6:F2} done", line);
            }

            // Both sinks saw the same thing.
            var written = fake.LogWriter.ToString();
            Assert.Contains(lines[0], written);
            Assert.Contains(lines[1], written);
        });
    }

    // ─── (c) both call forms compile against the new surface unchanged ────────────────────────────

    [Fact]
    public void EveryCallForm_StillCompilesAndEmits()
    {
        var fake = new FakePlatformServices();
        RunWithFake(fake, () =>
        {
            try
            {
                Logger.Initialize("logdir", LogLevel.Debug);

                var variable = "form-variable";

                Logger.Debug("form-literal-debug");                 // plain literal  -> string overload
                Logger.Info(variable);                              // variable       -> string overload
                Logger.Warning("literal " + $"concat {1}");         // concatenation  -> string overload,
                                                                    // still eager, still fine
                Logger.Error($"form-interpolated {2 + 3}");         // interpolation  -> handler overload
                Logger.Info(string.Concat("form-", "concat-call")); // method result  -> string overload

                Logger.Shutdown();
            }
            finally
            {
                Logger.Shutdown();
            }

            var lines = EmittedLines(fake);
            Assert.Contains(lines, l => l.EndsWith("[DEBUG] form-literal-debug"));
            Assert.Contains(lines, l => l.EndsWith("[ INFO] form-variable"));
            Assert.Contains(lines, l => l.EndsWith("[ WARN] literal concat 1"));
            Assert.Contains(lines, l => l.EndsWith("[ERROR] form-interpolated 5"));
            Assert.Contains(lines, l => l.EndsWith("[ INFO] form-concat-call"));
            Assert.All(lines, l => Assert.Matches(FormatContract, l));
        });
    }
}
