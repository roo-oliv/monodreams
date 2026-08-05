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

    /// The physics demo IS the collision showcase (colliders-as-entities): balls (RigidBody+Velocity
    /// bodies, each with its own convex collider) bounce inside four static wall collider entities, and
    /// the custom BallBounceSystem resolves off the BODY side of CollisionMessage (BodyA write-back,
    /// BodyB FloorTag — collider == body for a standalone demo entity). This is the CE-D live render-path
    /// smoke for that body-side consumer: it must render every frame AND hold a flat live heap while the
    /// detection grid rebuilds and the balls move perpetually — a per-frame retained allocation in the
    /// collision hot path (or a leaked collider entity) would fail (c).
    ///
    /// It is ALSO the live proof for the textured-mesh path (issue #43): the demo draws a 64×64
    /// screen-space quad from a 2×2 sheet as ONE textured mesh, and TexturedMeshUVCheckSystem reads the
    /// UI render target back on one frame to assert what the GPU actually painted — the four texel
    /// blocks land where the UVs say (correct mapping) and the pixels either side of the texel seam are
    /// pure (PointClamp, not bilinear). No unit test can assert that: it needs a real GraphicsDevice.
    [Fact]
    public async Task HeadlessPhysicsDemo_SelfTerminates_CapturesFrames_AndHoldsFlatHeap()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "physics",
            frames: 600,
            captureEvery: 120,
            sampleEvery: 30,
            timeoutSeconds: 120);

        // (acceptance) self-terminates with exit code 0, no human interaction.
        Assert.Equal(0, result.ExitCode);

        // (a) a log line proving the headless run was configured and completed.
        result.AssertLogContainsInOrder(
            "Headless run: screen='demos.physics'",
            "Headless run complete");

        // (b) a screenshot exists and is non-blank — proves the balls + boundary + collision-driven
        // motion actually rendered on the Main target (not the Examples no-op headless mode).
        result.AssertScreenshotNonBlank();

        // (c) the live managed heap stays flat across 600 frames of perpetual collision resolution +
        // per-frame broadphase-grid rebuild — the CE hot path must not retain per-frame allocations.
        // The textured-mesh self-check must not perturb this: its readback buffer is allocated once at
        // construction (before the first sample) and it runs on exactly one frame.
        result.AssertHeapFlat(maxGrowthRatio: 1.5, skipSamples: 2);

        // (d) the textured-mesh path actually painted the right pixels (issue #43). The check line is
        // emitted once, from a readback of the UI target; pass=True means all four texel blocks matched
        // their UVs AND both pixels flanking the texel seam were pure (point-sampled, not blended).
        result.AssertLogContains("TexturedMeshUVCheck");
        Assert.Contains(result.LogLines,
            line => line.Contains("TexturedMeshUVCheck") && line.Contains("pass=True"));
    }
}
