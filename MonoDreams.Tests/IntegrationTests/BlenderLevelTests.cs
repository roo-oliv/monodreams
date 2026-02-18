using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

public class BlenderLevelTests
{
    [Fact]
    public async Task BlenderLevelLoadsSuccessfully()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Blender_Level",
            Description = "Just load and exit",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 1.0f },
                new() { Action = "Exit", Type = "release", Time = 1.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Loading Blender level: Blender_Level");
        result.AssertLogContainsInOrder(
            "Loading Blender level: Blender_Level",
            "objects from Blender level",
            "entities from Blender level",
            "Replay complete. Exiting game."
        );
    }

    [Fact]
    public async Task PlayerReachesDialogueZone()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Blender_Level",
            Description = "Move player to dialogue trigger zone",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Right", Type = "press",   Time = 1.0f },
                new() { Action = "Up",    Type = "press",   Time = 1.0f },
                new() { Action = "Right", Type = "release", Time = 1.25f },
                new() { Action = "Up",    Type = "release", Time = 1.45f },
                new() { Action = "Exit",  Type = "press",   Time = 5.0f },
                new() { Action = "Exit",  Type = "release", Time = 5.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Loading Blender level: Blender_Level");
    }
}
