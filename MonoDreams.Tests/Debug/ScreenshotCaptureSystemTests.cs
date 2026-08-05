using System.Globalization;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System.Debug;

namespace MonoDreams.Tests.Debug;

/// <summary>
/// Protects the capture env contract — <c>MONODREAMS_SCREENSHOT</c>,
/// <c>MONODREAMS_SCREENSHOT_INTERVAL</c>, <c>MONODREAMS_SCREENSHOT_MAX_FRAMES</c> — of which
/// <see cref="ScreenshotCaptureSystem.FromEnvironment"/> is the single reader (see the debug-module
/// premise "`FromEnvironment` is the single owner of the capture env contract"). A second reader
/// would be a second dialect, so this is the one place the protocol is asserted.
///
/// <para>No <c>GraphicsDevice</c> is needed: the constructor and <c>FromEnvironment</c> only store the
/// device reference (nothing dereferences it until a capture reads the backbuffer), so
/// <c>null!</c> exercises the whole decision path with no graphics context. Everything observable —
/// the mkdir, the effective interval, the frame cap, the rejection error — is routed through
/// <see cref="PlatformServices.Current"/>, which a fake serves from dictionaries.</para>
///
/// <para>These tests mutate the global <see cref="PlatformServices.Current"/> holder, so they share
/// the non-parallel collection with the platform-seam tests and restore the previous holder in a
/// finally.</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class ScreenshotCaptureSystemTests : IDisposable
{
    private const string OutputDir = "/fake/debug";

    /// <summary>
    /// These assertions read the capture system's own Info/Warning/Error lines, so the process-global
    /// <see cref="Logger.MinimumLevel"/> must be at its default before each one — nothing but a fresh
    /// <c>Initialize</c> resets it, and a sibling test class deliberately raises it. An
    /// open-then-close pair on a throwaway sink restores it whatever ran before.
    /// </summary>
    public ScreenshotCaptureSystemTests() => ResetLoggerStatics();

    public void Dispose() => ResetLoggerStatics();

    private static void ResetLoggerStatics()
    {
        WithEnvironment(new FakeEnvironment(), _ =>
        {
            Logger.Shutdown();
            Logger.Initialize("scratch");
            Logger.Shutdown();
        });
    }

    /// <summary>
    /// In-memory <see cref="IPlatformServices"/> serving env vars from a dictionary and recording
    /// every side effect the capture system has: the mkdir and the log lines.
    /// </summary>
    private sealed class FakeEnvironment : IPlatformServices
    {
        public string BaseDirectory => "/fake/base/";
        public Dictionary<string, string> EnvVars { get; } = new();
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> CreatedDirectories { get; } = new();
        public List<string> ConsoleLines { get; } = new();

        public string GetEnvironmentVariable(string name) =>
            EnvVars.TryGetValue(name, out var v) ? v : null!;

        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) => throw new NotSupportedException();
        public void WriteAllText(string path, string contents) => throw new NotSupportedException();
        public void WriteAllBytes(string path, byte[] bytes) => Files[path] = bytes;
        public string ExportScene(string suggestedFileName, string contents) => throw new NotSupportedException();
        public void CreateDirectory(string path) => CreatedDirectories.Add(path);
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
        public void RunBackground(Action work) => work();
    }

    /// Installs the fake, runs the body, and always restores the previous holder — other tests
    /// (and the desktop default) share this static.
    private static void WithEnvironment(FakeEnvironment fake, Action<FakeEnvironment> body)
    {
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            body(fake);
        }
        finally
        {
            PlatformServices.Current = previous;
        }
    }

    private static FakeEnvironment Env(params (string name, string value)[] vars)
    {
        var fake = new FakeEnvironment();
        foreach (var (name, value) in vars) fake.EnvVars[name] = value;
        return fake;
    }

    /// The system's own initialization log line, which reports the EFFECTIVE format, interval and
    /// cap — the only externally visible form those private fields take.
    private static string InitLine(FakeEnvironment fake) =>
        Assert.Single(fake.ConsoleLines, l => l.Contains("ScreenshotCaptureSystem initialized"));

    // ── the request itself ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromEnvironment_ReturnsNull_WhenNothingRequested()
    {
        WithEnvironment(Env(), fake =>
        {
            Assert.Null(ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir));
            // Nothing was requested, so nothing was constructed — no mkdir, no log.
            Assert.Empty(fake.CreatedDirectories);
            Assert.Empty(fake.ConsoleLines);
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData(" off ")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEnvironment_ReturnsNull_WhenExplicitlyOff(string requested)
    {
        WithEnvironment(Env(("MONODREAMS_SCREENSHOT", requested)), fake =>
        {
            Assert.Null(ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir));
            Assert.Empty(fake.CreatedDirectories);
        });
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("true")]
    [InlineData("2")]
    [InlineData("mp4")]
    public void FromEnvironment_ReturnsNull_AndLogsAnError_ForAnUnknownMode(string requested)
    {
        WithEnvironment(Env(("MONODREAMS_SCREENSHOT", requested)), fake =>
        {
            // An unrecognised mode captures NOTHING — it never silently degrades to "capture
            // something", and it says why.
            Assert.Null(ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir));
            Assert.Empty(fake.CreatedDirectories);
            Assert.Contains(fake.ConsoleLines,
                l => l.Contains("[ERROR]") && l.Contains("is not a capture mode"));
        });
    }

    // ── format selection ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1")]
    [InlineData("png")]
    [InlineData("PNG")]
    [InlineData(" Png ")]
    public void FromEnvironment_SelectsPng_AndDefaultsToHalfSecondInterval(string requested)
    {
        WithEnvironment(Env(("MONODREAMS_SCREENSHOT", requested)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            Assert.Equal(CaptureFormat.Png, system.Format);
            Assert.True(system.IsEnabled, "An env-requested capture is enabled — nothing else switches it on.");
            // The historical PNG cadence, unchanged. Interpolated the same way the system does, so the
            // assertion is culture-symmetric rather than culture-dependent.
            Assert.Contains($"Format: {CaptureFormat.Png}", InitLine(fake));
            Assert.Contains($"interval: {0.5f}s", InitLine(fake));
            Assert.DoesNotContain("stopping after", InitLine(fake));
            // Constructing the system created its output directory (through the seam, no real disk).
            Assert.Equal(new[] { OutputDir }, fake.CreatedDirectories);
        });
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("rgba")]
    [InlineData("RAW")]
    public void FromEnvironment_SelectsRaw_AndDefaultsToEveryFrame(string requested)
    {
        WithEnvironment(Env(("MONODREAMS_SCREENSHOT", requested)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            Assert.Equal(CaptureFormat.Raw, system.Format);
            // Raw defaults to EVERY frame: the mode exists to produce a full-rate take, and an
            // inherited interval would silently make it a slideshow.
            Assert.Contains($"Format: {CaptureFormat.Raw}", InitLine(fake));
            Assert.Contains("interval: every frame", InitLine(fake));
            // …and it warns about the firehose it is, unprompted.
            Assert.Contains(fake.ConsoleLines,
                l => l.Contains("[ WARN]") && l.Contains("MiB per frame"));
        });
    }

    // ── interval override ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("raw", "0.25", 0.25f)]
    [InlineData("png", "2", 2f)]
    [InlineData("png", " 1.5 ", 1.5f)]
    public void FromEnvironment_IntervalOverride_ParsesInvariantCulture(string mode, string interval, float expected)
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", mode),
            ("MONODREAMS_SCREENSHOT_INTERVAL", interval)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            Assert.Contains($"interval: {expected}s", InitLine(fake));
        });
    }

    [Fact]
    public void FromEnvironment_IntervalOverride_AcceptsZeroAsEveryFrame()
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "png"),
            ("MONODREAMS_SCREENSHOT_INTERVAL", "0")), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            Assert.Contains("interval: every frame", InitLine(fake));
        });
    }

    [Theory]
    // A comma decimal separator must NOT be read as 0.25 — the parse is pinned to the invariant
    // culture, so a pt-BR machine and a US machine agree on what the variable means.
    [InlineData("0,25")]
    [InlineData("half")]
    [InlineData("-1")]
    [InlineData("")]
    public void FromEnvironment_IntervalOverride_IsIgnored_WhenUnparsableOrNegative(string interval)
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "png"),
            ("MONODREAMS_SCREENSHOT_INTERVAL", interval)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            // The format default stands rather than degrading to 0 (= every frame, a PNG firehose).
            Assert.Contains($"interval: {0.5f}s", InitLine(fake));
        });
    }

    [Fact]
    public void FromEnvironment_IntervalOverride_ParseIsUnaffectedByTheAmbientCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose decimal separator is a comma: "0.25" must still be a quarter second.
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            WithEnvironment(Env(
                ("MONODREAMS_SCREENSHOT", "raw"),
                ("MONODREAMS_SCREENSHOT_INTERVAL", "0.25")), fake =>
            {
                using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

                Assert.NotNull(system);
                Assert.Contains($"interval: {0.25f}s", InitLine(fake));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // ── frame cap ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromEnvironment_MaxFrames_Parses_AndIsReportedAtStartup()
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "raw"),
            ("MONODREAMS_SCREENSHOT_MAX_FRAMES", "600")), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            // The safety valve on a ~220 MB/s producer: it must be visible in the log of the run
            // that set it, so a capped run is distinguishable from a runaway one.
            Assert.Contains("stopping after 600 frames", InitLine(fake));
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("lots")]
    [InlineData("")]
    public void FromEnvironment_MaxFrames_IsIgnored_WhenNotAPositiveInteger(string cap)
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "raw"),
            ("MONODREAMS_SCREENSHOT_MAX_FRAMES", cap)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            Assert.DoesNotContain("stopping after", InitLine(fake));
        });
    }

    // ── the direct constructor keeps its own defaults ────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultsToPng_Uncapped_AndDisabled()
    {
        WithEnvironment(new FakeEnvironment(), fake =>
        {
            // The headless Demos host constructs one directly for its deterministic CaptureNow
            // channel; that path must stay PNG, uncapped, and OFF until a caller enables it.
            using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0f, OutputDir);

            Assert.Equal(CaptureFormat.Png, system.Format);
            Assert.False(system.IsEnabled);
            Assert.DoesNotContain("stopping after", InitLine(fake));
        });
    }

    [Fact]
    public void AnnotateFilename_IsUnsetByDefault()
    {
        WithEnvironment(new FakeEnvironment(), _ =>
        {
            using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0f, OutputDir);
            // The hook is opt-in: an unset annotation must not appear in a filename at all (the
            // PNG name shape is parsed by tooling).
            Assert.Null(system.AnnotateFilename);
        });
    }
}
