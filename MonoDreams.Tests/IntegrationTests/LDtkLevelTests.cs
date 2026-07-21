using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Post-PS5 the LDtk loader is <b>import-only</b> (not wired to live game boot), and the LDtk
/// <c>Level_0</c> is <b>not migrated</b> to a native <c>.mdscene</c> yet — its ~21k per-tile entities need
/// a native tile-layer batching primitive (a PS6 item), so a per-entity native scene would be an
/// unreasonable multi-MB artifact. Booting <c>Level_0</c> is therefore native-only with no native file:
/// it <b>fails loud</b> and no LDtk parse is attempted (the parser-asymmetry is closed — there is no
/// silent legacy fallback). The LDtk parser itself survives as import machinery (composed only in the
/// import op's <c>importMode</c> pipeline, unit-covered by <c>LevelImporterTests</c>).
/// </summary>
public class LDtkLevelTests
{
    [Fact]
    public async Task UnmigratedLevel_FailsLoud_WithNoSilentLdtkBoot()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartLevel = "Level_0",
            Description = "Level_0 has no native scene and LDtk boot is removed — fail loud, exit cleanly",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 1.0f },
                new() { Action = "Exit", Type = "release", Time = 1.1f },
            }
        });

        Assert.Equal(0, result.ExitCode);
        // The native-only dispatcher fails loud (no native scene) and does NOT attempt the LDtk path.
        result.AssertLogContains("the legacy LDtk loader is import-only");
        // The old LDtk-boot log line must NOT appear (the parser is not wired to boot).
        Assert.DoesNotContain(result.LogLines,
            line => line.Contains("Published") && line.Contains("tile spawn requests"));
    }
}
