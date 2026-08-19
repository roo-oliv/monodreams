using System.Globalization;
using System.Text.RegularExpressions;

namespace MonoDreams.Tests.IntegrationTests;

/// The headless Demos host's clock contract: under <c>--headless</c> the frames the pipeline sees
/// come off an injected fixed-step clock (<c>MonoDreams.Demos.HeadlessClock</c>), never the
/// wallclock. That is a precondition for using the observe-and-self-verify path as EVIDENCE —
/// headless deliberately runs with <c>IsFixedTimeStep = false</c> and VSync off (the max-speed
/// contract), so MonoGame's own <c>GameTime</c> carries the measured duration of the previous frame
/// and no two runs of the same demo would ever agree on <c>GameState.Time</c>/<c>TotalTime</c>, the
/// <c>[GT …]</c> stamp of a log line, or anything integrating over dt.
///
/// The run's observable here is the heap-sample line, which the host emits with the frame index AND
/// that frame's total game time (<c>Heap sample: frame=N gt=X bytes=…</c>) — the game clock made
/// readable from outside the process.
public class HeadlessClockTests
{
    /// Matches the host's heap-sample line. `gt` is captured as TEXT: the host formats it with the
    /// machine's culture, so a comma is a legal decimal separator here.
    private static readonly Regex HeapSample =
        new(@"Heap sample: frame=(?<frame>\d+) gt=(?<gt>[-\d.,]+) bytes=", RegexOptions.Compiled);

    private const int Frames = 120;
    private const int SampleEvery = 30;

    /// <summary>The step the headless host advances by: <c>Game.TargetElapsedTime</c>, i.e. the rate
    /// the WINDOWED path runs at.</summary>
    private const double StepSeconds = 1.0 / 60.0;

    /// <summary>
    /// The fixed-step half of the contract: game time advances by exactly one step per frame, so the
    /// gap between two samples is exactly the frame gap × the step — regardless of how long the
    /// machine actually took. A wallclock-driven run fails this outright: 120 frames at max speed
    /// elapse in a fraction of the 2 s of game time they represent.
    /// </summary>
    [Fact]
    public async Task HeadlessDemo_AdvancesGameTimeByAFixedStepPerFrame_NotByWallclock()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "camera",
            frames: Frames,
            captureEvery: 0,
            sampleEvery: SampleEvery,
            timeoutSeconds: 120);

        result.AssertExitedCleanly();

        // The host announces the injected clock, so a run's own log says which clock produced it.
        result.AssertLogContains("Headless clock: deterministic fixed step");

        var samples = ReadHeapSamples(result);
        Assert.True(samples.Count >= 3,
            $"Expected at least 3 heap samples over {Frames} frames, got {samples.Count}.");

        // Every sample against the first one — an accumulating drift (the wallclock signature) shows
        // up as a growing error rather than a single bad pair.
        var (baseFrame, baseGt) = samples[0];
        foreach (var (frame, gt) in samples)
        {
            var expected = (frame - baseFrame) * StepSeconds;
            var actual = gt - baseGt;
            // One rounding unit of slack: the host prints gt to 2 decimals, nothing more.
            Assert.True(Math.Abs(actual - expected) <= 0.011,
                $"Frame {frame}: game time advanced {actual:F3}s since frame {baseFrame}, " +
                $"but a fixed {StepSeconds:F5}s step over {frame - baseFrame} frames is {expected:F3}s. " +
                "Game time is tracking the wallclock.");
        }
    }

    /// <summary>
    /// The determinism half: two runs of the same demo for the same number of frames observe the
    /// SAME instants. This is the precheck every screenshot-identity gate rests on — if the clock
    /// itself differed between runs, no pixel comparison downstream could mean anything.
    /// </summary>
    [Fact]
    public async Task HeadlessDemo_RunTwice_ObservesTheIdenticalGameTimeSeries()
    {
        var first = await GameTestRunner.RunDemosAsync(
            screen: "camera", frames: Frames, captureEvery: 0, sampleEvery: SampleEvery, timeoutSeconds: 120);
        var second = await GameTestRunner.RunDemosAsync(
            screen: "camera", frames: Frames, captureEvery: 0, sampleEvery: SampleEvery, timeoutSeconds: 120);

        first.AssertExitedCleanly();
        second.AssertExitedCleanly();

        // Compared as the host PRINTED them (frame + the gt text), so the assertion covers the
        // rendered log stamp an agent actually reads, not just a parsed double.
        var firstSeries = ReadHeapSampleText(first);
        var secondSeries = ReadHeapSampleText(second);

        Assert.NotEmpty(firstSeries);
        Assert.Equal(firstSeries, secondSeries);
    }

    private static List<(int Frame, double Gt)> ReadHeapSamples(GameTestResult result)
    {
        var samples = new List<(int, double)>();
        foreach (var line in result.LogLines)
        {
            var match = HeapSample.Match(line);
            if (!match.Success) continue;
            var frame = int.Parse(match.Groups["frame"].Value, CultureInfo.InvariantCulture);
            // The host formats gt under the machine's culture; normalise before parsing so this test
            // reads the same on a comma-decimal machine as on a dot-decimal one.
            var gtText = match.Groups["gt"].Value.Replace(',', '.');
            samples.Add((frame, double.Parse(gtText, CultureInfo.InvariantCulture)));
        }

        return samples;
    }

    private static List<string> ReadHeapSampleText(GameTestResult result)
    {
        var series = new List<string>();
        foreach (var line in result.LogLines)
        {
            var match = HeapSample.Match(line);
            if (match.Success)
                series.Add($"frame={match.Groups["frame"].Value} gt={match.Groups["gt"].Value}");
        }

        return series;
    }
}
