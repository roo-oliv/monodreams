using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Post-PS5 the Blender level is <b>import-only</b>: the Blender parser is no longer wired to live game
/// boot. The reference game now boots the <b>migrated native</b> <c>Content/Levels/Blender_Level.mdscene</c>
/// through the native-first <c>LevelLoadRequestSystem</c> → <c>SceneReaderSystem</c> (the shipped reader),
/// not the Blender parser. These formerly-"Blender boot" tests were converted to assert the native boot;
/// the Blender parser survives as import machinery (exercised by the export op that generated the scene,
/// and unit-tested by <c>LevelImporterTests</c> / <c>MigratedLevelTests</c>).
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
        // The Blender parser is import-only now — its boot log must NOT appear.
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
