using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// The reference game's "Level 1" is the committed <b>native</b> scene
/// <c>Content/Levels/Blender_Level.mdscene</c> — its origin was a Blender export, but it is now a native
/// v3 scene the game owns (migrated to v3 by <c>monodreams migrate</c>: its legacy <c>camera</c> block was
/// lifted into an ordinary <c>core.Camera</c> entity — CM). It boots through the native-first
/// <c>LevelLoadRequestSystem</c> → <c>SceneReaderSystem</c> (the shipped reader). The Blender import module
/// was <b>deleted in wave BR</b>, so there is no Blender parser at all; these tests gate that the committed
/// scene boots natively (the migration round-trip is unit-tested by <c>MigratedLevelTests</c>). The class
/// keeps its name after the committed <c>Blender_Level</c> scene it exercises.
/// </summary>
public class BlenderLevelTests
{
    [Fact]
    public async Task BlenderLevelBootsNative()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Blender_Level",
            Description = "Boot the migrated native Blender_Level and exit",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 1.0f },
                new() { Action = "Exit", Type = "release", Time = 1.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        // Native-first resolved the id to a bundled .mdscene and the shipped native reader loaded it —
        // NOT the Blender parser (which no longer runs at boot). The reader logs synchronously from
        // inside the probe, so "Loaded scene" precedes the dispatcher's "resolved" line.
        result.AssertLogContains("resolved to a native .mdscene");
        result.AssertLogContainsInOrder(
            "Loaded scene 'Levels/Blender_Level.mdscene'",
            "Replay complete. Exiting game."
        );
        // There is no Blender parser anymore (deleted in wave BR) — its old boot log must never appear;
        // the native reader path is the only one that ran.
        Assert.DoesNotContain(result.LogLines, line => line.Contains("Loading Blender level"));
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

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Loaded scene 'Levels/Blender_Level.mdscene'");
    }
}
