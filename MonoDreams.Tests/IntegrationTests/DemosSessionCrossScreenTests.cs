using MonoDreams.LevelEditor.Channel;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// TD closes the TB-A "Demos cross-screen limitation": the Demos host now creates the host-scoped
/// <c>EditorSession</c>, resolves a project context, and binds every demo screen to a scene, so a
/// cross-screen tab activation SURVIVES the <c>ScreenController.LoadScreen</c> — exactly like the Examples
/// <see cref="SessionCrossScreenTests"/>, but through the Demos host. A <c>tab:open physics-demo</c> op on
/// the launcher opens the physics demo's scene tab; physics-demo is bound to a DIFFERENT screen, so it is a
/// cross-screen activation (snapshot the launcher tab → pending → plain <c>LoadScreen(demos.physics)</c>),
/// and the destination overlay binds the SAME surviving session and consumes the pending activation.
///
/// <para>A SINGLE cross-screen switch is scripted so the per-screen op-replay never loops (the op is a
/// no-op on the destination, which is already current). In the <see cref="ContentTreeGuardCollection"/>
/// like the other editor-enabled spawned runs; the isolated temp project root the runner pins is what the
/// Demos host resolves.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class DemosSessionCrossScreenTests
{
    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    [Fact]
    public async Task TabOpen_ActivatesABoundDemoSceneCrossScreen_TheSessionSurvivesTheScreenSwitch()
    {
        var result = await GameTestRunner.RunDemosAsync(
            screen: "launcher", frames: 120, captureEvery: 0, sampleEvery: 0,
            timeoutSeconds: 120, environment: EditorEnv,
            editorOpPlan: new EditorOpPlan
            {
                Description = "settle on the launcher, then open the physics demo's scene tab cross-screen",
                Ops = new List<EditorOp>
                {
                    // tab:open <sceneId> → EditorOverlay.SelectScene; physics-demo is bound to demos.physics
                    // (a DIFFERENT screen than the launcher), so it is a cross-screen activation.
                    new() { Frame = 20, Kind = EditorOpKind.ToolbarAction, Action = "tab:open physics-demo" },
                },
                TailFrames = 40,
            });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Editor run flag active");
        // The whole path, in order: the launcher composed the session-bound overlay → the cross-screen
        // activation fired (proving the session drives the host switch) → the physics demo composed its
        // overlay (binding the SAME surviving session) → it consumed the pending activation.
        result.AssertLogContainsInOrder(
            "Editor overlay composed on DemoLauncherScreen",
            "TB-A: opening scene tab 'physics-demo'",
            "Editor overlay composed on PhysicsDemoScreen",
            "TB-A: consumed pending activation");
    }
}
