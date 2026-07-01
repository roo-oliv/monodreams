using System;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave-6 editor run configuration (level-editor premise "The editor run flag opts
/// game screens into the overlay and boots RunMode = Edit; default off is byte-identical"):
/// <c>--editor</c> / <c>MONODREAMS_EDITOR=1</c> parse, and the boot-time run mode at the
/// GameState level — no window spawned, exactly the composition the desktop head performs
/// (<c>EditorRunFlag.IsEnabled</c> → <c>ScreenController.State.RunMode = InitialRunMode(...)</c>).
/// </summary>
public class EditorRunFlagTests
{
    // ---- Parse: the launch arg ----

    [Fact]
    public void LaunchArg_EnablesTheEditor()
    {
        Assert.True(EditorRunFlag.IsEnabled(new[] { "--editor" }, _ => null));
        Assert.True(EditorRunFlag.IsEnabled(new[] { "--headless", "--editor" }, _ => null));
    }

    // ---- Parse: the environment variable (a Rider run configuration's env section) ----

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" 1 ")] // tolerate whitespace from shell/IDE quoting
    public void EnvironmentVariable_EnablesTheEditor(string value)
    {
        Assert.True(EditorRunFlag.IsEnabled(
            Array.Empty<string>(),
            name => name == EditorRunFlag.EnvironmentVariable ? value : null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    public void EnvironmentVariable_OtherValues_LeaveTheEditorOff(string? value)
    {
        Assert.False(EditorRunFlag.IsEnabled(
            Array.Empty<string>(),
            name => name == EditorRunFlag.EnvironmentVariable ? value : null));
    }

    // ---- Back-compat: the flag defaults OFF ----

    [Fact]
    public void NoArgNoEnv_TheFlagIsOff()
    {
        Assert.False(EditorRunFlag.IsEnabled(null, null));
        Assert.False(EditorRunFlag.IsEnabled(new[] { "--headless" }, _ => null));
    }

    // ---- Boot run mode, at the GameState level (no window) ----

    [Fact]
    public void FlagOn_BootsRunModeEdit_AtTheGameStateLevel()
    {
        // Exactly the desktop head's composition: a freshly constructed GameState (what
        // ScreenController builds) still defaults to Play — the foundation back-compat premise —
        // and the host applies the flag as an explicit opt-in mutation.
        var state = new GameState(new GameTime());
        Assert.Equal(RunMode.Play, state.RunMode);

        var enabled = EditorRunFlag.IsEnabled(new[] { "--editor" }, _ => null);
        state.RunMode = EditorRunFlag.InitialRunMode(enabled);
        Assert.Equal(RunMode.Edit, state.RunMode);
    }

    [Fact]
    public void FlagOff_RunModeStaysPlay()
    {
        var state = new GameState(new GameTime());
        var enabled = EditorRunFlag.IsEnabled(Array.Empty<string>(), _ => null);
        state.RunMode = EditorRunFlag.InitialRunMode(enabled);
        Assert.Equal(RunMode.Play, state.RunMode);
    }
}
