using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;

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

    // ── capture source (window backbuffer vs a named render target) ──────────────────────────────

    [Fact]
    public void FromEnvironment_DefaultsToTheWindowBackbuffer()
    {
        WithEnvironment(Env(("MONODREAMS_SCREENSHOT", "png")), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            // Unset target == the historical behaviour, byte for byte: the file follows the window.
            Assert.Null(system.CaptureTarget);
            Assert.Contains("source: window backbuffer", InitLine(fake));
        });
    }

    [Theory]
    [InlineData("window")]
    [InlineData("WINDOW")]
    [InlineData(" window ")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromEnvironment_Window_IsTheBackbuffer(string requested)
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "png"),
            ("MONODREAMS_SCREENSHOT_TARGET", requested)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            Assert.Null(system.CaptureTarget);
            Assert.Contains("source: window backbuffer", InitLine(fake));
        });
    }

    [Theory]
    [InlineData("Main", RenderTargetID.Main)]
    [InlineData("ui", RenderTargetID.UI)]
    [InlineData("HUD", RenderTargetID.HUD)]
    [InlineData(" Scroll ", RenderTargetID.Scroll)]
    [InlineData("editor", RenderTargetID.Editor)]
    public void FromEnvironment_NamesARenderTarget_CaseInsensitively(string requested, RenderTargetID expected)
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "png"),
            ("MONODREAMS_SCREENSHOT_TARGET", requested)), fake =>
        {
            using var system = ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir);

            Assert.NotNull(system);
            // The whole point: the capture reads a FIXED-resolution surface, so the file geometry no
            // longer follows the window (or a resize, or the letterbox).
            Assert.Equal(expected, system.CaptureTarget);
            Assert.Contains($"source: {expected} render target", InitLine(fake));
        });
    }

    [Theory]
    [InlineData("backbuffer")]
    [InlineData("Minimap")]
    [InlineData("Main,UI")]   // a flags-style combination is not a render target
    [InlineData("0")]         // and neither is the enum's numeric alias — env values are NAMES
    [InlineData("-1")]
    public void FromEnvironment_ReturnsNull_AndLogsAnError_ForAnUnknownTarget(string requested)
    {
        WithEnvironment(Env(
            ("MONODREAMS_SCREENSHOT", "png"),
            ("MONODREAMS_SCREENSHOT_TARGET", requested)), fake =>
        {
            // An unreadable source captures NOTHING rather than silently falling back to the window:
            // evidence at the wrong geometry looks right and compares with nothing.
            Assert.Null(ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir));
            Assert.Empty(fake.CreatedDirectories);
            Assert.Contains(fake.ConsoleLines,
                l => l.Contains("[ERROR]") && l.Contains("is not a capture source"));
        });
    }

    [Fact]
    public void FromEnvironment_TargetIsIgnored_WhenNoCaptureWasRequested()
    {
        WithEnvironment(Env(("MONODREAMS_SCREENSHOT_TARGET", "UI")), fake =>
        {
            // Naming a target is not a request to capture — MONODREAMS_SCREENSHOT still decides.
            Assert.Null(ScreenshotCaptureSystem.FromEnvironment(null!, OutputDir));
            Assert.Empty(fake.CreatedDirectories);
            Assert.Empty(fake.ConsoleLines);
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
            Assert.Null(system.CaptureTarget);
            Assert.DoesNotContain("stopping after", InitLine(fake));
        });
    }

    // ── the render-pass observation socket ───────────────────────────────────────────────────────

    [Fact]
    public void WindowCapture_NeverTouchesTheRenderSocket()
    {
        var previousSink = MasterRenderSystem.RenderedTargetSink;
        try
        {
            MasterRenderSystem.RenderedTargetSink = null;
            WithEnvironment(new FakeEnvironment(), _ =>
            {
                using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0f, OutputDir);

                // A window-mode capture needs no render-pass observation, so the render path keeps
                // paying nothing but the null check it already had.
                Assert.Null(MasterRenderSystem.RenderedTargetSink);
            });
        }
        finally
        {
            MasterRenderSystem.RenderedTargetSink = previousSink;
        }
    }

    [Fact]
    public void TargetCapture_PlugsIntoTheRenderSocket_AndUnplugsOnDispose()
    {
        var previousSink = MasterRenderSystem.RenderedTargetSink;
        try
        {
            MasterRenderSystem.RenderedTargetSink = null;
            WithEnvironment(new FakeEnvironment(), _ =>
            {
                var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0f, OutputDir,
                    CaptureFormat.Png, maxFrames: 0, captureTarget: RenderTargetID.UI);

                // Target capture resolves its surface from the passes that actually ran — screens
                // register their targets nowhere, so this socket IS the lookup.
                Assert.NotNull(MasterRenderSystem.RenderedTargetSink);

                system.Dispose();

                // …and a disposed capture must not keep a dead screen's targets (or itself) reachable
                // from a static delegate for the rest of the process.
                Assert.Null(MasterRenderSystem.RenderedTargetSink);
            });
        }
        finally
        {
            MasterRenderSystem.RenderedTargetSink = previousSink;
        }
    }

    // ── the latched target's lifetime (screen switches, resizes) ─────────────────────────────────

    /// <summary>
    /// A stand-in <see cref="RenderTarget2D"/>. The latch only ever stores the reference and asks
    /// whether it is disposed — it dereferences nothing until a capture reads pixels, which needs a
    /// <c>GraphicsDevice</c> no unit test has. Same ctor-less trick as <c>SpriteFlipTests.StubTexture</c>,
    /// with the finalizer suppressed (it would dereference the null graphics device and take the test
    /// host down from the finalizer thread).
    /// </summary>
    private static RenderTarget2D StubTarget()
    {
        var target = (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D));
        GC.SuppressFinalize(target);
        return target;
    }

    /// <summary>
    /// Makes a target report <c>IsDisposed</c> — what a screen switch or a window resize does to a real
    /// one. <c>Dispose()</c> itself is not an option on a device-less stub (it dereferences the null
    /// device), so the backing flag is set directly and the result is asserted: if a MonoGame/KNI
    /// version renames the field, this fails loudly here rather than silently turning the regressions
    /// below into tests of nothing.
    /// </summary>
    private static RenderTarget2D MarkDisposed(RenderTarget2D target)
    {
        for (var type = (Type?)target.GetType(); type != null && !target.IsDisposed; type = type.BaseType)
        {
            var field = type.GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.FieldType == typeof(bool)) field.SetValue(target, true);
        }

        Assert.True(target.IsDisposed,
            "could not mark a stub RenderTarget2D disposed — GraphicsResource's backing field was " +
            "renamed, so this fixture (not the capture system) needs updating.");
        return target;
    }

    /// <summary>
    /// Publishes through the real socket, exactly as <c>MasterRenderSystem</c> does at the top of a
    /// pass — the capture's handler is private on purpose, and this is the only door into it.
    /// </summary>
    private static void PublishPass(RenderTargetID id, RenderTarget2D target) =>
        MasterRenderSystem.RenderedTargetSink!(id, target);

    /// Runs a body with the render socket empty, restoring whatever was installed before.
    private static void WithEmptyRenderSocket(Action body)
    {
        var previousSink = MasterRenderSystem.RenderedTargetSink;
        try
        {
            MasterRenderSystem.RenderedTargetSink = null;
            body();
        }
        finally
        {
            MasterRenderSystem.RenderedTargetSink = previousSink;
        }
    }

    [Fact]
    public void ResolvedTarget_IsReplaced_WhenTheLatchedOneWasDisposed()
    {
        WithEmptyRenderSocket(() => WithEnvironment(new FakeEnvironment(), _ =>
        {
            using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0.5f, OutputDir,
                CaptureFormat.Png, maxFrames: 0, captureTarget: RenderTargetID.UI);

            // Frame 1: the current screen's UI pass publishes, and the capture latches it.
            var doomed = StubTarget();
            PublishPass(RenderTargetID.UI, doomed);
            Assert.Same(doomed, system.ResolveSourceTarget());

            // …then the screen switches (or the window is resized): the latched target is torn down and
            // the new screen's pass publishes a fresh one. An interval capture reads MANY frames after
            // the latch, so this whole exchange happens between two reads.
            MarkDisposed(doomed);
            var replacement = StubTarget();
            PublishPass(RenderTargetID.UI, replacement);

            // THE regression: a `??=` latch refuses every replacement once a dead target sits in the
            // slot, so the capture writes nothing for the rest of the run.
            Assert.Same(replacement, system.ResolveSourceTarget());
        }));
    }

    [Fact]
    public void ResolvedTarget_IsDropped_WhenItWasDisposedWithoutAReplacement()
    {
        WithEmptyRenderSocket(() => WithEnvironment(new FakeEnvironment(), fake =>
        {
            using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0.5f, OutputDir,
                CaptureFormat.Png, maxFrames: 0, captureTarget: RenderTargetID.UI);

            // A target torn down after the last publish of the frame (the resize that makes the editor
            // chrome rebuild its target, with the read landing before the next pass): nothing to read…
            var doomed = StubTarget();
            PublishPass(RenderTargetID.UI, doomed);
            MarkDisposed(doomed);
            Assert.Null(system.ResolveSourceTarget());
            Assert.Contains(fake.ConsoleLines,
                l => l.Contains("[DEBUG]") && l.Contains("was torn down"));
            // …and it is a self-healing gap, not the "this screen has no such pass" dead end.
            Assert.DoesNotContain(fake.ConsoleLines, l => l.Contains("no render pass has drawn"));

            // …and the slot was CLEARED, not merely refused, so the next pass can fill it.
            var replacement = StubTarget();
            PublishPass(RenderTargetID.UI, replacement);
            Assert.Same(replacement, system.ResolveSourceTarget());
        }));
    }

    [Fact]
    public void ResolvedTarget_KeepsTheFirstPublisher_WhileItIsAlive()
    {
        WithEmptyRenderSocket(() => WithEnvironment(new FakeEnvironment(), fake =>
        {
            using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0.5f, OutputDir,
                CaptureFormat.Png, maxFrames: 0, captureTarget: RenderTargetID.Main);

            // One screen, two passes for the same id (the camera demo's world pass, then its minimap):
            // the FIRST is the primary one, and a live latch is never displaced.
            var worldPass = StubTarget();
            var minimapPass = StubTarget();
            PublishPass(RenderTargetID.Main, worldPass);
            PublishPass(RenderTargetID.Main, minimapPass);

            Assert.Same(worldPass, system.ResolveSourceTarget());

            // A pass for another id is not this capture's business either.
            PublishPass(RenderTargetID.UI, StubTarget());
            Assert.Same(worldPass, system.ResolveSourceTarget());
            Assert.DoesNotContain(fake.ConsoleLines, l => l.Contains("was torn down"));
        }));
    }

    [Fact]
    public void ResolvedTarget_WarnsOncePerGap_WhenNoPassEverDrawsTheTarget()
    {
        WithEmptyRenderSocket(() => WithEnvironment(new FakeEnvironment(), fake =>
        {
            using var system = new ScreenshotCaptureSystem((GraphicsDevice)null!, 0.5f, OutputDir,
                CaptureFormat.Png, maxFrames: 0, captureTarget: RenderTargetID.Editor);

            // A screen with no Editor pass: the capture writes nothing and says so — once, not sixty
            // times a second.
            Assert.Null(system.ResolveSourceTarget());
            Assert.Null(system.ResolveSourceTarget());

            Assert.Single(fake.ConsoleLines,
                l => l.Contains("[ WARN]") && l.Contains("no render pass has drawn the Editor target"));
        }));
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
