using MonoDreams.Input;
using MonoDreams.LevelEditor.Channel;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Protects the Wave-8a premise "the editor overlay is universal": under the editor run flag
/// (<c>MONODREAMS_EDITOR=1</c>) EVERY Examples screen — the level-selection menu and the infinite
/// runner included, not just the game screen — composes the overlay through the pipeline
/// registrar, observable as the per-screen composition log naming the <c>editor.*</c> entries.
/// Flag-off behavior is protected by the whole pre-existing suite (nothing editor-related is
/// constructed and the pipelines are behaviourally identical).
///
/// <para>The menu run exits through the headless editor-op channel (the menu runs no
/// <c>InputReplaySystem</c>, so the op driver owns the session end); the runner run exits through
/// the ordinary replay auto-exit-on-drain.</para>
/// </summary>
public class UniversalOverlayTests
{
    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    [Fact]
    public async Task LevelSelection_UnderTheEditorFlag_ComposesTheOverlay()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "LevelSelection",
            Description = "Menu under MONODREAMS_EDITOR=1 composes the editor overlay; the op driver exits",
            Commands = new List<InputReplayCommand>(),
        },
        timeoutSeconds: 60,
        environment: EditorEnv,
        // The menu has no InputReplaySystem, so the editor-op channel holds the session and
        // requests exit after its (single, no-op) op + tail drains.
        editorOpPlan: new EditorOpPlan
        {
            Description = "idle a few frames, then exit",
            Ops = new List<EditorOp> { new() { Frame = 10, Kind = EditorOpKind.MoveCursor, X = 100, Y = 100 } },
            TailFrames = 5,
        });

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Editor run flag active");
        // The menu composed the overlay: its composition log lists the editor.* entries of BOTH
        // pipelines, including the systems panel and the menu-specific freeze policy split.
        result.AssertLogContains("Editor overlay composed on LevelSelectionScreen");
        result.AssertLogContains("editor.systemsPanel");
        result.AssertLogContains("editor.selection");
        result.AssertLogContains("Editor-op plan complete");
    }

    [Fact]
    public async Task InfiniteRunner_UnderTheEditorFlag_ComposesTheOverlay_WithItsOwnCursorPipeline()
    {
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "InfiniteRunner",
            Description = "Runner under MONODREAMS_EDITOR=1 composes the overlay (self-sufficient cursor)",
            Commands = new List<InputReplayCommand>
            {
                new() { Action = "Exit", Type = "press",   Time = 2.0f },
                new() { Action = "Exit", Type = "release", Time = 2.1f },
            }
        },
        timeoutSeconds: 60,
        environment: EditorEnv);

        Assert.Equal(0, result.ExitCode);
        result.AssertLogContains("Editor overlay composed on InfiniteRunnerScreen");
        // The runner has no cursor pipeline of its own — the overlay supplied one.
        result.AssertLogContains("editor.cursorInput");
        result.AssertLogContains("editor.cursorPosition");
        result.AssertLogContains("editor.systemsPanel");
        result.AssertLogContainsInOrder(
            "Loading InfiniteRunner screen",
            "Replay complete. Exiting game.");
    }
}
