using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

public class LDtkLevelTests
{
    [Fact]
    public async Task LDtkLevelLoadsSuccessfully()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Level_0",
            Description = "Load LDtk Level_0 and exit",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 1.0f },
                new() { Action = "Exit", Type = "release", Time = 1.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContainsInOrder(
            "Replay complete. Exiting game."
        );
    }
}
