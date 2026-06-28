using System;
using System.Collections.Generic;
using System.IO;
using MonoDreams.Input;
using MonoDreams.Platform;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.Platform;

/// <summary>
/// Protects the Phase-1 portability seam: engine source routes every filesystem /
/// environment / console / log-sink call through <see cref="PlatformServices.Current"/>
/// rather than touching <c>File</c>/<c>Directory</c>/<c>AppDomain</c>/<c>Environment</c>/
/// <c>Console</c> directly. A fake implementation lets these tests assert the routing
/// happens — with no real disk — while <see cref="DesktopPlatformServices"/> is checked
/// to reproduce the historical real-filesystem behaviour.
///
/// These tests mutate the global <see cref="PlatformServices.Current"/> holder (and the
/// static <see cref="Logger"/>), so the class is non-parallel and every test restores
/// the desktop default in a finally.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class PlatformServicesTests
{
    /// <summary>
    /// In-memory <see cref="IPlatformServices"/>: records calls and serves storage from
    /// a dictionary so engine code can be exercised with no real filesystem.
    /// </summary>
    private sealed class FakePlatformServices : IPlatformServices
    {
        public string BaseDirectory { get; init; } = "/fake/base/";
        public Dictionary<string, string> EnvVars { get; } = new();
        public Dictionary<string, string> Files { get; } = new();
        public List<string> ConsoleLines { get; } = new();
        public List<string> CreatedDirectories { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public int BackgroundRuns { get; private set; }

        public string GetEnvironmentVariable(string name) =>
            EnvVars.TryGetValue(name, out var v) ? v : null;

        // Forward-slash join is enough for an in-memory fake and keeps assertions stable
        // across OSes (the real path conventions are DesktopPlatformServices' job).
        public string CombinePath(params string[] paths) => string.Join("/", paths);

        public bool FileExists(string path) => Files.ContainsKey(path);

        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);

        public void WriteAllText(string path, string contents) => Files[path] = contents;

        public void WriteAllBytes(string path, byte[] bytes) =>
            Files[path] = Convert.ToBase64String(bytes);

        public void CreateDirectory(string path) => CreatedDirectories.Add(path);

        public TextWriter OpenLogWriter(string directory, string fileName)
        {
            CreatedDirectories.Add(directory);
            return LogWriter;
        }

        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);

        // Run inline so a single-threaded host (and a deterministic test) sees the
        // effect synchronously — exactly what the web/WASM impl will do.
        public void RunBackground(Action work)
        {
            BackgroundRuns++;
            work();
        }
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

    [Fact]
    public void PlatformServices_Current_DefaultsToDesktop()
    {
        Assert.IsType<DesktopPlatformServices>(PlatformServices.Current);
    }

    [Fact]
    public void PlatformServices_Current_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => PlatformServices.Current = null);
    }

    [Fact]
    public void InputReplayPlan_TryLoad_RoutesThroughPlatformServices_AndParses()
    {
        var fake = new FakePlatformServices();
        var path = fake.CombinePath("debugdir", "input_replay.json");
        fake.Files[path] =
            "{\"description\":\"seam\",\"startLevel\":\"Level_0\",\"screenshots\":true," +
            "\"commands\":[{\"action\":\"Jump\",\"type\":\"press\",\"time\":0.5}]}";

        RunWithFake(fake, () =>
        {
            var plan = InputReplayPlan.TryLoad("debugdir");

            Assert.NotNull(plan);
            Assert.Equal("seam", plan.Description);
            Assert.Equal("Level_0", plan.StartLevel);
            Assert.True(plan.Screenshots);
            Assert.Single(plan.Commands);
            Assert.Equal("Jump", plan.Commands[0].Action);
        });
    }

    [Fact]
    public void InputReplayPlan_TryLoad_ReturnsNull_WhenFileMissing_ViaSeam()
    {
        var fake = new FakePlatformServices(); // no files seeded
        RunWithFake(fake, () => Assert.Null(InputReplayPlan.TryLoad("debugdir")));
    }

    [Fact]
    public void Logger_RoutesLogSink_AndConsole_ThroughPlatformServices()
    {
        var fake = new FakePlatformServices();
        RunWithFake(fake, () =>
        {
            // Logger is a process-global singleton; reset around this test.
            Logger.Shutdown();
            try
            {
                Logger.Initialize("logdir");
                Logger.Info("seam-marker-line");
                Logger.Shutdown(); // flush the buffered writer

                // The log sink came from the fake (no file touched), and the console
                // echo went through the fake too — proving both halves of the seam.
                Assert.Contains("seam-marker-line", fake.LogWriter.ToString());
                Assert.Contains(fake.ConsoleLines,
                    line => line.Contains("seam-marker-line"));
                // Logger asked the platform to create the log directory.
                Assert.Contains("logdir", fake.CreatedDirectories);
            }
            finally
            {
                Logger.Shutdown();
            }
        });
    }

    [Fact]
    public void DesktopPlatformServices_RoundTripsRealFilesystem()
    {
        var svc = new DesktopPlatformServices();
        var dir = svc.CombinePath(Path.GetTempPath(), "monodreams_platform_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            svc.CreateDirectory(dir);
            var textPath = svc.CombinePath(dir, "round.txt");
            svc.WriteAllText(textPath, "hello-desktop");
            Assert.True(svc.FileExists(textPath));
            Assert.Equal("hello-desktop", svc.ReadAllText(textPath));

            var bytesPath = svc.CombinePath(dir, "round.bin");
            var payload = new byte[] { 1, 2, 3, 4 };
            svc.WriteAllBytes(bytesPath, payload);
            Assert.True(svc.FileExists(bytesPath));
            Assert.Equal(payload, File.ReadAllBytes(bytesPath));

            // BaseDirectory and env lookup map to the real process.
            Assert.False(string.IsNullOrEmpty(svc.BaseDirectory));
            const string envName = "MONODREAMS_PLATFORM_TEST_VAR";
            Environment.SetEnvironmentVariable(envName, "env-value");
            try
            {
                Assert.Equal("env-value", svc.GetEnvironmentVariable(envName));
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, null);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
