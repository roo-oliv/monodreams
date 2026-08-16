namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Integration coverage for the host wiring of <c>MONODREAMS_KEEP_AWAKE</c>: the flag only helps if a
/// host actually holds the assertion for the whole run and releases it at shutdown, and that wiring
/// lives in the hosts (<c>MonoDreams.Demos/Game1.cs</c>, <c>MonoDreams.Examples.Desktop/Game1.cs</c>),
/// where a unit test cannot see it. The unattended agentic run is exactly the run macOS suspends —
/// App Nap on a hidden window, then display/idle sleep — so "the flag was read and honoured" is
/// worth a spawned process to assert.
/// </summary>
public class KeepAwakeHostTests
{
    [Fact]
    public async Task Host_HoldsAndReleasesTheAssertion_WhenTheEnvironmentAsks()
    {
        // Three frames: this asserts the boot/shutdown wiring, not anything about rendering.
        var result = await GameTestRunner.RunDemosAsync(
            screen: "camera",
            frames: 3,
            captureEvery: 0,
            sampleEvery: 0,
            timeoutSeconds: 300,
            environment: new Dictionary<string, string> { ["MONODREAMS_KEEP_AWAKE"] = "1" });

        Assert.Equal(0, result.ExitCode);

        if (OperatingSystem.IsMacOS())
            // Held before the run and released after it — in that order, in the run's own log, which
            // is where an agent debugging a stalled overnight run will look for it.
            result.AssertLogContainsInOrder(
                "Keep-awake: NSProcessInfo activity held",
                "Headless run complete",
                "Keep-awake: NSProcessInfo activity released");
        else
            // Elsewhere the request is a logged no-op: the run still completes, and the log says the
            // machine was never actually kept awake rather than implying it was.
            result.AssertLogContains("Keep-awake requested");
    }

    [Fact]
    public async Task Host_AssertsNothing_WhenTheEnvironmentIsSilent()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "camera",
            frames: 3,
            captureEvery: 0,
            sampleEvery: 0,
            timeoutSeconds: 300);

        Assert.Equal(0, result.ExitCode);
        // Default-off is the contract: an ordinary run must not touch the user's power management,
        // and must not log as though it had.
        Assert.DoesNotContain(result.LogLines, line => line.Contains("Keep-awake"));
    }
}
