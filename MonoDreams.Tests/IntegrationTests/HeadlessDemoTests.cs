namespace MonoDreams.Tests.IntegrationTests;

/// Integration coverage for the headless Demos observe-and-self-verify path (issue #28).
/// A single headless run of the camera demo exercises every assertion type: a log line,
/// a non-blank screenshot, and live-heap flatness over a static scene.
public class HeadlessDemoTests
{
    [Fact]
    public async Task HeadlessCameraDemo_SelfTerminates_CapturesFrames_AndHoldsFlatHeap()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "camera",
            frames: 600,
            captureEvery: 120,
            sampleEvery: 30,
            timeoutSeconds: 120);

        // (acceptance) self-terminates with exit code 0, no human interaction.
        Assert.Equal(0, result.ExitCode);

        // (a) a log line proving the headless run was configured and completed.
        result.AssertLogContainsInOrder(
            "Headless run: screen='demos.camera'",
            "Headless run complete");

        // (b) a screenshot exists and is non-blank — proves Draw actually rendered,
        // distinguishing this from the Examples no-op headless mode.
        result.AssertScreenshotNonBlank();

        // (c) the live managed heap stays flat across 600 frames of a static scene —
        // a retained-object leak (e.g. #27) would make this fail.
        result.AssertHeapFlat(maxGrowthRatio: 1.5, skipSamples: 1);
    }

    [Fact]
    public async Task HeadlessUiDemo_SelfTerminates_CapturesFrames_AndHoldsFlatHeap()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "ui",
            frames: 600,
            captureEvery: 120,
            sampleEvery: 30,
            timeoutSeconds: 120);

        // (acceptance) self-terminates with exit code 0, no human interaction.
        Assert.Equal(0, result.ExitCode);

        // (a) a log line proving the headless run was configured and completed.
        result.AssertLogContainsInOrder(
            "Headless run: screen='demos.ui'",
            "Headless run complete");

        // (b) a screenshot exists and is non-blank — proves the UI widgets
        // (buttons, text inputs, checkboxes, etc.) actually rendered on the Main target.
        result.AssertScreenshotNonBlank();

        // (c) the live managed heap stays flat across 600 frames of a static scene.
        result.AssertHeapFlat(maxGrowthRatio: 1.5, skipSamples: 1);
    }
}
