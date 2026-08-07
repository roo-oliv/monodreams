using MonoDreams.Input;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Post-PS5 the LDtk loader is <b>import-only</b> (not wired to live game boot), and the LDtk
/// <c>Level_0</c> is <b>not migrated</b> to a native <c>.mdscene</c> yet — its ~21k per-tile entities need
/// a native tile-layer batching primitive (a PS6 item), so a per-entity native scene would be an
/// unreasonable multi-MB artifact. Booting <c>Level_0</c> is therefore native-only with no native file:
/// it <b>fails loud</b> and no LDtk parse is attempted (the parser-asymmetry is closed — there is no
/// silent legacy fallback).
///
/// <para>Since issue #54 the boot dispatcher (<c>LevelLoadRequestSystem</c>) does not even <i>compile</i>
/// against LDtk: the legacy load moved to <c>level-ldtk</c>'s own <c>LDtkLevelLoadSystem</c>, composed
/// solely by the import op. The second test here is the counterpart to the first — it proves that
/// refactored import path still parses tiles and spawns entities end-to-end.</para>
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
        result.AssertLogContains("No native scene 'Content/Levels/Level_0.mdscene' found");
        result.AssertLogContains("author or migrate it to a native .mdscene");
        // The old LDtk-boot log line must NOT appear (the parser is not wired to boot).
        Assert.DoesNotContain(result.LogLines,
            line => line.Contains("Published") && line.Contains("tile spawn requests"));
    }

    /// <summary>
    /// The refactored LDtk import path (issue #54) still works end-to-end. The headless import op
    /// (<c>MONODREAMS_EXPORT_SCENE=Level_0</c>) boots the Game screen in <c>importMode</c>, which composes
    /// <c>LDtkLevelLoadSystem</c> → it sets <c>LDtkLevelDataComponent</c> → both parsers (which now
    /// subscribe to <i>that</i> component instead of <c>CurrentLevelComponent</c>) parse and publish
    /// <c>EntitySpawnRequest</c>s → the factories build entities (reading the layer opacity / grid size
    /// off the new <c>ldtk:</c> <c>CustomFields</c> channel instead of the deleted
    /// <c>EntitySpawnRequest.Layer</c>). The op driver owns the exit: <c>Game1</c> returns from
    /// <c>Initialize</c> in the export branch and exits after the first <c>Update</c>, so the replay plan
    /// below only exists to satisfy the runner (its commands never fire).
    /// </summary>
    [Fact]
    public async Task ImportOp_StillParsesTilesAndSpawnsEntities()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
            {
                StartLevel = "Level_0",
                Description = "Headless import op re-parses the legacy LDtk Level_0 (the op driver owns the exit)",
                Commands = new List<InputReplayCommand>(),
            },
            // Level_0 is ~21k tiles: generous headroom for the parse + the 26k-entity native write.
            timeoutSeconds: 180,
            environment: new Dictionary<string, string> { ["MONODREAMS_EXPORT_SCENE"] = "Level_0" });

        result.AssertExitedCleanly();
        // The import-only loader ran (LevelLoadRequestSystem's native-only dispatch is NOT composed here)…
        result.AssertLogContains("Received request to import legacy LDtk level 'Level_0'");
        // …the tile parser walked the tile/auto layers off LDtkLevelDataComponent…
        result.AssertLogContains("tile spawn requests");
        // …and the entity parser walked the entity instances off the same component.
        result.AssertLogContains("Finished parsing entities");
    }
}
