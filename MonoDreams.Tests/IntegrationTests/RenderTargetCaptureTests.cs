using System.Text.RegularExpressions;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Integration coverage for <c>MONODREAMS_SCREENSHOT_TARGET</c>: capturing a NAMED render target at
/// its own fixed resolution instead of the window backbuffer. Only the headless Demos host can show
/// this end to end — it needs real render passes filling real targets (see the debug premise
/// "Headless Demos renders every frame; capture reads the backbuffer").
///
/// <para>The UI demo is the screen that makes the claim visible: its <c>Scroll</c> target is
/// <b>360x220</b> while the backbuffer is <b>1280x720</b>, so the captured geometry cannot be
/// explained by the window, the viewport, or the letterbox — it can only come from the target. Raw
/// format is used because the frame name carries the geometry, which is exactly the assertion; the
/// run is capped at a handful of frames (~309 KiB each) and deletes its blobs afterwards.</para>
/// </summary>
public class RenderTargetCaptureTests
{
    /// <summary>The Scroll render target of the UI demo screen (<c>UiDemoScreen.ScrollViewW/H</c>).</summary>
    private const int ScrollTargetWidth = 360;
    private const int ScrollTargetHeight = 220;

    /// <summary>The headless Demos backbuffer — what a window-mode capture would have produced, and
    /// what this test proves the file does NOT follow.</summary>
    private const int BackBufferWidth = 1280;
    private const int BackBufferHeight = 720;

    private static readonly Regex RawFrameName =
        new(@"^raw_(\d{6})_(\d+)x(\d+)_(\d{8})\.rgba$", RegexOptions.Compiled);

    [Fact]
    public async Task TargetCapture_WritesFramesAtTheTargetResolution_NotTheWindowSize()
    {
        const int frames = 15;
        const int cap = 4;

        var result = await GameTestRunner.RunDemosAsync(
            screen: "ui",
            frames: frames,
            captureEvery: 0,   // no periodic PNG — this run is about the raw target capture
            sampleEvery: 0,    // no heap sampling: a forced GC per sample perturbs frame pacing
            timeoutSeconds: 300,
            environment: new Dictionary<string, string>
            {
                ["MONODREAMS_SCREENSHOT"] = "raw",
                ["MONODREAMS_SCREENSHOT_TARGET"] = "Scroll",
                ["MONODREAMS_SCREENSHOT_MAX_FRAMES"] = cap.ToString(),
            });

        var blobs = Directory.GetFiles(result.DebugDir, "*.rgba");
        try
        {
            // (acceptance) the run still self-terminates cleanly with target capture wired in.
            Assert.Equal(0, result.ExitCode);
            result.AssertLogContainsInOrder(
                "ScreenshotCaptureSystem initialized",
                "Headless run complete");

            // (a) the env contract landed on the env-built instance: the run RECORDS what its files
            // are pictures of, which is the only way a later reader can tell the two sources apart.
            Assert.Contains(result.LogLines, line =>
                line.Contains("ScreenshotCaptureSystem initialized")
                && line.Contains("Format: Raw")
                && line.Contains("source: Scroll render target"));

            // …while the host's own deterministic channel is untouched by the variable — it still
            // reads the window, which is the default this feature had to leave alone.
            Assert.Contains(result.LogLines, line =>
                line.Contains("ScreenshotCaptureSystem initialized")
                && line.Contains("source: window backbuffer"));

            // (b) the cap held on the target path too.
            Assert.Equal(cap, blobs.Length);

            var parsed = blobs
                .Select(Path.GetFileName)
                .Select(name =>
                {
                    var m = RawFrameName.Match(name!);
                    Assert.True(m.Success, $"'{name}' does not match the raw frame name contract.");
                    return (
                        counter: int.Parse(m.Groups[1].Value),
                        width: int.Parse(m.Groups[2].Value),
                        height: int.Parse(m.Groups[3].Value));
                })
                .OrderBy(f => f.counter)
                .ToList();

            // (c) THE point of the feature: every frame is the target's fixed resolution, not the
            // window's. A window-mode run of this same screen produces 1280x720 frames.
            Assert.All(parsed, f =>
            {
                Assert.Equal(ScrollTargetWidth, f.width);
                Assert.Equal(ScrollTargetHeight, f.height);
            });
            Assert.DoesNotContain(parsed, f => f.width == BackBufferWidth && f.height == BackBufferHeight);

            // (d) and the bytes agree with the name — a full uncompressed RGBA8888 frame of the
            // target, so a reader indexing by geometry is never lied to.
            Assert.All(blobs, path =>
                Assert.Equal(ScrollTargetWidth * ScrollTargetHeight * 4, new FileInfo(path).Length));

            // (e) contiguous from 000000: no frame was skipped waiting for the target to resolve
            // after the first pass published it.
            Assert.Equal(Enumerable.Range(0, cap), parsed.Select(f => f.counter));
        }
        finally
        {
            foreach (var path in blobs)
            {
                try { File.Delete(path); }
                catch (IOException) { /* best effort */ }
            }
        }
    }
}
