using System.Text.RegularExpressions;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Integration coverage for the raw frame-capture path: <c>MONODREAMS_SCREENSHOT=raw</c> over the
/// headless Demos host, which is the only place the capture can be observed end-to-end (it needs a
/// real <c>GraphicsDevice</c> with a composited backbuffer to read — see the debug premise
/// "Headless Demos renders every frame; capture reads the backbuffer").
///
/// <para>Raw capture is a ~220 MB/s producer at 1280x720, so this run is deliberately short and
/// capped, and it deletes its own blobs afterwards. Do not raise the frame counts: at 3.5 MiB a frame
/// the disk cost is the test's dominant cost.</para>
/// </summary>
public class RawFrameCaptureTests
{
    /// <summary>
    /// <c>raw_{counter:D6}_{width}x{height}_{gametimeMs:D8}.rgba</c> — the self-describing name that
    /// lets a tool index frames by game time with no sidecar manifest and no culture-dependent parse.
    /// </summary>
    private static readonly Regex RawFrameName =
        new(@"^raw_(\d{6})_(\d+)x(\d+)_(\d{8})\.rgba$", RegexOptions.Compiled);

    [Fact]
    public async Task RawCapture_WritesOneBlobPerFrame_AndStopsAtTheFrameCap()
    {
        // 40 draws, capture capped at 25 — the cap must trip *inside* the run so its log line is
        // observable, and 25 x 3.5 MiB is ~88 MiB of churn, which is as much as this test may cost.
        const int frames = 40;
        const int cap = 25;

        var result = await GameTestRunner.RunDemosAsync(
            screen: "camera",
            frames: frames,
            captureEvery: 0,   // no periodic PNG — this run is about the raw path
            sampleEvery: 0,    // no heap sampling: a forced GC per sample perturbs frame pacing
            // The spawned `dotnet run` rebuilds Demos when the tree is cold, and that build bills
            // against this timeout — the 40-frame run itself takes seconds.
            timeoutSeconds: 300,
            environment: new Dictionary<string, string>
            {
                ["MONODREAMS_SCREENSHOT"] = "raw",
                ["MONODREAMS_SCREENSHOT_MAX_FRAMES"] = cap.ToString(),
            });

        var blobs = Directory.GetFiles(result.DebugDir, "*.rgba");
        try
        {
            // (acceptance) the run still self-terminates cleanly with capture wired in.
            Assert.Equal(0, result.ExitCode);
            result.AssertLogContainsInOrder(
                "ScreenshotCaptureSystem initialized",
                "Headless run complete");

            // (a) the env contract landed, on ONE line so it is unambiguously the env-built
            // instance (the headless host also constructs its own PNG capture): raw format, every
            // frame, capped. The every-frame interval is the direct evidence there is no interval
            // gate in front of the capture — i.e. full rate.
            Assert.Contains(result.LogLines, line =>
                line.Contains("ScreenshotCaptureSystem initialized")
                && line.Contains("Format: Raw")
                && line.Contains("interval: every frame")
                && line.Contains($"stopping after {cap} frames"));

            // (b) exactly the cap: not one blob more (the valve held) and not one fewer (no frame
            // was dropped in the first `cap` draws).
            Assert.Equal(cap, blobs.Length);

            var parsed = blobs
                .Select(path => Path.GetFileName(path))
                .Select(name =>
                {
                    var m = RawFrameName.Match(name);
                    Assert.True(m.Success, $"'{name}' does not match the raw frame name contract.");
                    return (
                        counter: int.Parse(m.Groups[1].Value),
                        width: int.Parse(m.Groups[2].Value),
                        height: int.Parse(m.Groups[3].Value),
                        milliseconds: int.Parse(m.Groups[4].Value));
                })
                .OrderBy(f => f.counter)
                .ToList();

            // (c) contiguous from 000000. The counter advances once per successful write, so a gap
            // (or a short set) would mean a swallowed write; combined with (b) it says the first
            // `cap` draws each produced exactly one blob.
            Assert.Equal(Enumerable.Range(0, cap), parsed.Select(f => f.counter));

            // (d) the geometry in the name is the headless virtual resolution — a 1x1 backbuffer
            // would make every captured frame meaningless (the debug-module premise).
            Assert.All(parsed, f =>
            {
                Assert.Equal(1280, f.width);
                Assert.Equal(720, f.height);
            });

            // (e) the embedded game time only ever moves forward — that is what makes it usable as a
            // seek index into the frame set.
            for (var i = 1; i < parsed.Count; i++)
            {
                Assert.True(parsed[i].milliseconds >= parsed[i - 1].milliseconds,
                    $"Frame {parsed[i].counter} is stamped {parsed[i].milliseconds}ms, BEFORE frame " +
                    $"{parsed[i - 1].counter}'s {parsed[i - 1].milliseconds}ms — game time went backwards.");
            }

            // …and once past warmup, strictly: every steady-state frame is a distinct, later moment.
            // The Demos headless host runs a VARIABLE timestep (no VSync, IsFixedTimeStep = false),
            // so the spacing is not fixed — observed steady state is ~24-28ms per frame on a 1280x720
            // readback, while the first few frames (JIT + content load) can land inside the same
            // millisecond, which is a warmup artefact and not a capture defect (the counter, not the
            // timestamp, is what keeps those frames distinct files).
            const int warmupFrames = 5;
            var steady = parsed.Skip(warmupFrames).ToList();
            for (var i = 1; i < steady.Count; i++)
            {
                Assert.True(steady[i].milliseconds > steady[i - 1].milliseconds,
                    $"Frame {steady[i].counter} is stamped {steady[i].milliseconds}ms, not after frame " +
                    $"{steady[i - 1].counter}'s {steady[i - 1].milliseconds}ms — capture is not advancing per frame.");
            }

            // (f) every blob is a full uncompressed RGBA8888 frame — 1280*720*4.
            Assert.All(blobs, path => Assert.Equal(1280 * 720 * 4, new FileInfo(path).Length));

            // (g) the cap announced itself, with the byte total. This is the shared stop mechanism
            // the write-failure (full-disk) path also uses.
            result.AssertLogContains($"Frame capture stopped at the {cap}-frame cap");

            // (h) the run's Dispose summary — proof the raw accounting survived to shutdown.
            result.AssertLogContains($"Raw capture finished: {cap} frames");
        }
        finally
        {
            // ~88 MiB of scratch blobs; leaving them in the temp dir would be antisocial.
            foreach (var path in blobs)
            {
                try { File.Delete(path); }
                catch (IOException) { /* best effort */ }
            }
        }
    }
}
