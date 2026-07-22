namespace MonoDreams.Tests.IntegrationTests;

/// Integration coverage for the audio demo over the headless Demos observe-and-self-verify
/// path (issue #28). Audio is inaudible in a headless run (and the backend may be absent
/// entirely on CI — ContentAudioPlayer degrades to a silent no-op), so the demo's
/// Logger.Info start/stop lines are the observable: the screen logs its playback intent
/// regardless of whether a hardware instance actually started.
public class HeadlessAudioDemoTests
{
    [Fact]
    public async Task HeadlessAudioDemo_PlaysOneShotLoopAndJukeboxCut_LoggingEachStartAndStop()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "audio",
            frames: 600,
            captureEvery: 120,
            sampleEvery: 30,
            timeoutSeconds: 120);

        // (acceptance) self-terminates with exit code 0, no human interaction.
        Assert.Equal(0, result.ExitCode);

        // (a) the demo's scripted boot sequence exercises all three playback idioms, in order:
        // the wind loop starts on load (AudioSourceComponent Loop=true), the one-shot click
        // fires at frame 30 (PlaySoundRequest), the jukebox starts at frame 90 and is cut
        // mid-play at frame 300 (non-loop AudioSourceComponent, State flipped to Stopped).
        // The riff is ~10s of wall-clock playback, so the frame-300 cut is genuinely mid-play
        // at any realistic headless frame rate.
        result.AssertLogContainsInOrder(
            "Headless run: screen='demos.audio'",
            "Audio demo: wind loop started.",
            "Audio demo: one-shot click fired.",
            "Audio demo: jukebox started.",
            "Audio demo: jukebox cut mid-play.",
            "Headless run complete");

        // (b) a screenshot exists and is non-blank — the HUD (header, sidebar, status panel)
        // rendered even though the audio itself is unobservable.
        result.AssertScreenshotNonBlank();

        // (c) the live managed heap stays flat once the scene reaches steady state. Skip the
        // samples up to the frame-300 jukebox cut (sound buffers load lazily at first play:
        // wind on load, jukebox at frame 90); after the cut only the wind loop reconciles,
        // so a per-frame retained allocation in the AudioSystem hot path would show as growth.
        result.AssertHeapFlat(maxGrowthRatio: 1.5, skipSamples: 11);
    }
}
