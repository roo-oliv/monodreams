using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// End-to-end proof of PS4 <b>native-first level loading + bundling</b>: the real (headless, editor-off)
/// game boots the committed native scene <c>Content/Levels/sample.mdscene</c> through
/// <c>LoadLevelRequest("sample")</c>. This exercises the whole chain — the MGCB <c>/copy:</c> bundling
/// (the file must land at <c>&lt;ContentRoot&gt;/Levels/sample.mdscene</c> for <c>TitleContainer</c> to
/// find it), the native-first probe in <c>LevelLoadRequestSystem</c>, and the generalized
/// <c>SceneReaderSystem</c> reconstructing the entities — with NO editor composed. If bundling failed,
/// the probe would miss and the run would attempt (and fail) the LDtk path for "sample" instead.
/// </summary>
public class NativeSceneBootTests
{
    [Fact]
    public async Task NativeSceneBootsViaLoadLevelRequest_AndBundlingIsTitleContainerReadable()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "sample",
            Description = "Boot the committed native sample.mdscene via native-first LoadLevelRequest",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 1.0f },
                new() { Action = "Exit", Type = "release", Time = 1.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        // The dispatcher resolved the level native-first (LDtk path skipped) …
        result.AssertLogContains("resolved to a native .mdscene");
        // … and the native reader reconstructed the 2 committed entities (bundling + TitleContainer read).
        result.AssertLogContainsInOrder(
            "Native scene found for level 'sample'",
            "Loaded scene",
            "Replay complete. Exiting game."
        );
    }
}
