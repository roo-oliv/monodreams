#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The "editor everywhere" run configuration flag — THE way into the editor: when active, a host
/// (the desktop head's <c>Game1</c>) composes the <see cref="EditorOverlay"/> into every
/// editor-capable game screen and boots the transport <b>Paused</b>
/// (<see cref="RunMode.Edit"/>). The editor is then always visible; the toolbar's Play/Pause +
/// Restart transport buttons (see <see cref="EditorTransport"/>) drive the game — no key toggles
/// the editor away. When inactive (the default), screens compose without the overlay and behave
/// exactly as before the flag existed.
///
/// <para>Two equivalent switches, both settable from an IDE run configuration (e.g. Rider:
/// "Program arguments: <c>--editor</c>" or "Environment variables:
/// <c>MONODREAMS_EDITOR=1</c>"):</para>
/// <list type="bullet">
///   <item>the launch argument <see cref="LaunchArg"/> (<c>--editor</c>), or</item>
///   <item>the environment variable <see cref="EnvironmentVariable"/>
///   (<c>MONODREAMS_EDITOR</c>) set to <c>1</c> or <c>true</c> (case-insensitive).</item>
/// </list>
///
/// <para>Boot-in-Edit deliberately does NOT change <see cref="GameState"/>'s constructed default
/// (still <see cref="RunMode.Play"/> — the foundation back-compat premise): the host applies
/// <see cref="InitialRunMode"/> to <c>ScreenController.State</c> after construction, an explicit
/// opt-in mutation.</para>
/// </summary>
public static class EditorRunFlag
{
    /// <summary>The launch argument that activates the editor run configuration.</summary>
    public const string LaunchArg = "--editor";

    /// <summary>The environment variable (value <c>1</c> or <c>true</c>) that activates it.</summary>
    public const string EnvironmentVariable = "MONODREAMS_EDITOR";

    /// <summary>
    /// Pure parse: is the editor run configuration active for these launch
    /// <paramref name="args"/> / this environment? Pass the host's environment reader
    /// (e.g. <c>Environment.GetEnvironmentVariable</c>); both inputs tolerate null.
    /// </summary>
    public static bool IsEnabled(IEnumerable<string>? args, Func<string, string?>? getEnvironmentVariable)
    {
        if (args != null && args.Contains(LaunchArg, StringComparer.Ordinal)) return true;

        var value = getEnvironmentVariable?.Invoke(EnvironmentVariable)?.Trim();
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The run mode the host boots into: <see cref="RunMode.Edit"/> when the flag is active
    /// (the transport boots Paused — the designer lands editing), <see cref="RunMode.Play"/>
    /// otherwise.
    /// </summary>
    public static RunMode InitialRunMode(bool editorEnabled) => editorEnabled ? RunMode.Edit : RunMode.Play;
}
