using MonoDreams.Input;
using MonoDreams.LevelEditor.Channel;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Protects the TB-A host-scoped <c>EditorSession</c> END-TO-END through the real Examples host: under the
/// editor run flag the session is created once in <c>Game1</c> and passed to every screen, so a
/// cross-screen tab activation (the Scenes-panel / <c>tab:open</c> op) SURVIVES the <c>ScreenController.LoadScreen</c>
/// that tears down the outgoing screen's world + overlay — the new screen's overlay BINDS the same session
/// and CONSUMES the pending activation instead of starting fresh. This is the wiring the deterministic
/// <c>EditorSessionTests</c> can only model with a fake rebind; here the whole path runs in a spawned process.
///
/// <para>A SINGLE cross-screen switch is scripted (menu → runner) so the per-screen op-replay never loops
/// (the op is a no-op on the destination, which is already current), and the destination's op driver owns
/// the exit. In the <see cref="ContentTreeGuardCollection"/> like the other editor-enabled spawned runs.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class SessionCrossScreenTests
{
    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    [Fact]
    public async Task TabOpen_ActivatesABoundSceneCrossScreen_TheSessionSurvivesTheScreenSwitch()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "LevelSelection",
            Description = "Menu under MONODREAMS_EDITOR=1; a tab:open op activates the runner's scene tab cross-screen",
            Commands = new List<InputReplayCommand>(),
        },
        timeoutSeconds: 90,
        environment: EditorEnv,
        editorOpPlan: new EditorOpPlan
        {
            Description = "settle on the menu, then open the InfiniteRunner scene tab cross-screen",
            Ops = new List<EditorOp>
            {
                // A named op routed through EditorOverlay.DispatchNamedAction → tab:open <sceneId>, which
                // opens/activates that scene's tab; infinite_runner is bound to a DIFFERENT screen, so it is
                // a cross-screen activation (snapshot the menu tab → pending → LoadScreen(InfiniteRunner)).
                new() { Frame = 15, Kind = EditorOpKind.ToolbarAction, Action = "tab:open infinite_runner" },
            },
            // Generous tail so the destination screen boots + its overlay consumes the pending activation
            // before the destination's op driver requests exit (the outgoing menu driver is disposed by the
            // LoadScreen swap, so the destination owns the exit).
            TailFrames = 30,
        });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Editor run flag active");
        // The whole path, in order: the menu composed the session-bound overlay → the cross-screen
        // activation fired (proving the session drives the host switch) → the destination screen composed
        // its overlay (binding the SAME surviving session) → it consumed the pending activation.
        result.AssertLogContainsInOrder(
            "Editor overlay composed on LevelSelectionScreen",
            "TB-A: opening scene tab 'infinite_runner'",
            "Editor overlay composed on InfiniteRunnerScreen",
            "TB-A: consumed pending activation");
    }
}
