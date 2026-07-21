using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// The reference game's "Level 1" is the committed <b>native</b> scene
/// <c>Content/Levels/Blender_Level.mdscene</c> — its origin was a Blender export, but it is now a native
/// scene the game owns. It boots through the native-first <c>LevelLoadRequestSystem</c> →
/// <c>SceneReaderSystem</c> (the shipped reader).
///
/// <para><b>TODO(CM-C): temporarily asserts the fail-loud.</b> Under CM (camera-as-entity) the version
/// guard refuses a version-2 scene that still carries a legacy <c>camera</c> block (run
/// <c>monodreams migrate</c>). The committed <c>Blender_Level.mdscene</c> is exactly such a file, and
/// committed content stays v2 this wave — CM-C's <c>monodreams migrate</c> lifts its camera block into a
/// <c>Camera</c> entity + bumps it to v3 in-repo. Until then, booting it fails loud (the reader logs the
/// migrate hint and re-throws, crashing the boot). These tests therefore assert the boot FAILS for now;
/// once CM-C migrates the committed scene, restore the boot-success + player-movement assertions from git
/// history. The unit-level reconstruction is still covered by
/// <c>MigratedLevelTests.CommittedBlenderLevel_BootsThroughTheShippedReader_YieldingPlayerAndNpcs</c>
/// (which applies the camera lift in-test).</para>
/// </summary>
public class BlenderLevelTests
{
    [Fact]
    public async Task BlenderLevel_UnmigratedCameraBlock_FailsLoudAtBoot()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Blender_Level",
            Description = "Boot the committed v2-with-camera Blender_Level and observe the CM fail-loud",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 1.0f },
                new() { Action = "Exit", Type = "release", Time = 1.1f },
            }
        });

        // TODO(CM-C): the committed Blender_Level is v2-with-camera → the CM version guard refuses it and
        // the boot crashes (non-zero exit). After CM-C migrates it to v3, restore the boot-success
        // assertions ("resolved to a native .mdscene" + "Loaded scene 'Levels/Blender_Level.mdscene'").
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task BlenderLevel_MovementDoesNotCrash()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Blender_Level",
            Description = "Boot native, move the player, exit cleanly",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Right", Type = "press",   Time = 1.0f },
                new() { Action = "Right", Type = "release", Time = 1.25f },
                new() { Action = "Exit",  Type = "press",   Time = 3.0f },
                new() { Action = "Exit",  Type = "release", Time = 3.1f },
            }
        });

        // TODO(CM-C): same as above — the unmigrated committed scene refuses at boot. Restore the
        // boot-success + player-movement assertions once CM-C migrates Blender_Level to v3.
        Assert.NotEqual(0, result.ExitCode);
    }
}
