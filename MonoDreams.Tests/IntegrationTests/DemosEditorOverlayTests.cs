namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Extends the Wave-8a premise "the editor overlay is universal" across hosts: under the editor
/// run flag (<c>MONODREAMS_EDITOR=1</c>) EVERY Demos screen — the launcher menu and all four
/// module demos — composes the <c>EditorOverlay</c> through the pipeline registrar, observable as
/// the per-screen composition log naming the <c>editor.*</c> entries. The Demos host honours the
/// flag under <c>--headless</c> too (headless Demos renders every frame — it is the
/// observe-and-self-verify channel, so the editor shell lands in the captured frames).
///
/// <para>Flag-off behavior is protected by <see cref="HeadlessDemoTests"/> (nothing
/// editor-related is constructed, the pipelines are behaviourally identical) plus the explicit
/// absence assertion below; <c>GameTestRunner.RunDemosAsync</c> pins <c>MONODREAMS_EDITOR=0</c>
/// unless a test opts in, so a developer's exported flag can never perturb those runs.</para>
///
/// <para>In the <see cref="ContentTreeGuardCollection"/>: an editor-enabled spawned suite, bracketed by
/// the real-content-tree tripwire (PF-E hardening). Its Demos head passes a null project context, so it
/// cannot write the source tree anyway — the guard is defence-in-depth.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class DemosEditorOverlayTests
{
    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    [Theory]
    [InlineData("launcher", "DemoLauncherScreen")]
    [InlineData("camera", "CameraDemoScreen")]
    [InlineData("physics", "PhysicsDemoScreen")]
    [InlineData("dialogue", "DialogueDemoScreen")]
    [InlineData("ui", "UiDemoScreen")]
    public async Task DemoScreen_UnderTheEditorFlag_ComposesTheOverlay(string screen, string screenName)
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen, frames: 15, captureEvery: 0, sampleEvery: 0,
            timeoutSeconds: 120, environment: EditorEnv);

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Editor run flag active");
        // The screen composed the overlay: its composition log lists the editor.* entries of BOTH
        // pipelines (update: mode toggle / gizmo / systems panel; draw: selection / chrome pass).
        result.AssertLogContains($"Editor overlay composed on {screenName}");
        result.AssertLogContains("editor.keys");
        result.AssertLogContains("editor.cameraNav");
        result.AssertLogContains("editor.gizmo");
        result.AssertLogContains("editor.systemsPanel");
        result.AssertLogContains("editor.selection");
        result.AssertLogContains("editor.renderChrome");
        // TD (report 1): the Demos host now RESOLVES a project context (the isolated temp root the runner
        // pins), so the Scenes panel is no longer "Project: (unresolved) … (no scenes)".
        result.AssertLogContains("Project resolved");
        // TD (report 1): with a resolved project the universal palette composes on Demos (empty assetRoots
        // is legal — no crash; exit 0 above also guards this).
        result.AssertLogContains("editor.palette");
        // TD (report 1): the session's boot tab is NAMED (the launcher's scene id), never the "untitled"
        // fallback the null-context Demos host used to seed.
        result.AssertLogContains("active tab 'launcher'");
        Assert.DoesNotContain(result.LogLines, l => l.Contains("active tab 'untitled'"));
        // Booting in Edit + rendering the shell must not break headless self-termination.
        result.AssertLogContains("Headless run complete");
    }

    [Fact]
    public async Task DemoScreen_FlagOff_ComposesNoOverlay()
    {
        var result = await GameTestRunner.RunDemosAsync(
            "camera", frames: 15, captureEvery: 0, sampleEvery: 0);

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Headless run complete");
        Assert.DoesNotContain(result.LogLines, l => l.Contains("Editor overlay composed"));
        Assert.DoesNotContain(result.LogLines, l => l.Contains("Editor run flag active"));
    }
}
