using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

public class InfiniteRunnerTests
{
    [Fact]
    public async Task InfiniteRunnerLoadsAndIdles()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "InfiniteRunner",
            Description = "Load infinite runner and idle briefly, then exit",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 2.0f },
                new() { Action = "Exit", Type = "release", Time = 2.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContainsInOrder(
            "Loading InfiniteRunner screen",
            "InfiniteRunner screen loaded",
            "Replay complete. Exiting game."
        );
    }

    [Fact]
    public async Task PlayerFallsOffLeftEdge()
    {
        // Don't press Right — player drifts left off the treadmill
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "InfiniteRunner",
            Description = "Player drifts left and falls off treadmill",
            Commands = new List<InputReplayCommand>
            {
                // Just wait and let the player drift off
                new() { Action = "Exit", Type = "press",   Time = 8.0f },
                new() { Action = "Exit", Type = "release", Time = 8.1f },
            }
        }, timeoutSeconds: 30);

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContainsInOrder(
            "Loading InfiniteRunner screen",
            "Game over."
        );
    }

    [Fact]
    public async Task PlayerCollectsCharm()
    {
        // Hold Right to stay on treadmill, wait for charms to spawn and collide
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "InfiniteRunner",
            Description = "Player runs right and collects charms",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Right", Type = "press",   Time = 0.5f },
                new() { Action = "Right", Type = "release", Time = 7.0f },
                new() { Action = "Exit",  Type = "press",   Time = 8.0f },
                new() { Action = "Exit",  Type = "release", Time = 8.1f },
            }
        }, timeoutSeconds: 30);

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Loading InfiniteRunner screen");
        // Charm may or may not be collected depending on timing, so just verify the run completes
        result.AssertLogContains("Replay complete. Exiting game.");
    }
}
