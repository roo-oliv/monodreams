using Microsoft.Xna.Framework.Input;
using MonoDreams.State;
using MonoDreams.System.Cursor;
using MonoDreams.System.Input;

namespace MonoDreams.Demos;

/// <summary>
/// The keyboard half of the demos' <b>deterministic-input protocol</b> — the seam the mouse already
/// had (<c>CursorInputSystem.SkipHardwareRead</c>) and the keyboard did not.
///
/// <para><b>Why it exists.</b> The byte-identity precheck (issue #119, contract items 49/68) claims
/// that a headless demo run under the protocol has deterministic INPUT. Pinning only the mouse left
/// every demo screen's keyboard systems reading <see cref="Keyboard.GetState"/> straight off the
/// hardware: the headless window is hidden best-effort (SDL hide, plus a macOS-only focus-steal
/// hint), so a key held while a run happens to own focus moves the camera demo's ball / advances the
/// dialogue / types into the UI demo's text field in ONE of two runs — a byte diff that reads as a
/// behaviour change and is really the developer's keyboard.</para>
///
/// <para><b>The seam.</b> Every demo screen's keyboard reader calls <see cref="Read"/> instead of
/// <see cref="Keyboard.GetState"/>; engine readers that already own the
/// <c>SkipHardwareRead</c> / provider seam are flipped by <see cref="Engage"/>. Off the protocol
/// <see cref="Read"/> IS <see cref="Keyboard.GetState"/>, so a windowed demo is unchanged — this
/// costs a bool test per read and nothing else. <c>DeterministicClockTests</c> lints that no demo
/// screen source calls <see cref="Keyboard.GetState"/> directly, so a keyboard reader added later
/// cannot re-open the hole in silence.</para>
/// </summary>
public static class DemoKeyboard
{
    /// <summary>The line a run emits when the protocol engages — the precheck asserts it, so losing
    /// the wiring is a red test rather than an intermittent byte diff.</summary>
    public const string EngagedLog = "Deterministic input: hardware reads skipped";

    /// <summary>Whether the protocol is engaged for this process (set once, by <see cref="Engage"/>).
    /// Never turned back off: the protocol covers a whole run, including a screen transition.</summary>
    public static bool SkipHardwareRead { get; private set; }

    /// <summary>The keyboard every demo screen reads. Neutral (no key down) once the protocol is
    /// engaged, the real hardware otherwise.</summary>
    public static KeyboardState Read() => SkipHardwareRead ? default : Keyboard.GetState();

    /// <summary>
    /// Engages the protocol for a screen: the shared keyboard gate above, the screen's cursor
    /// pipeline, and any hardware keyboard reader that owns its own <c>SkipHardwareRead</c> (the
    /// screen's <see cref="AKeyboardInputHandlingSystem"/> subclasses and the editor's own key
    /// reader). Called from the screen's pipeline construction under the same condition as the
    /// cursor leg — an editor op plan is present.
    /// </summary>
    /// <param name="screen">The screen id, so the log line says which screen engaged it.</param>
    /// <param name="cursor">The screen's cursor input system (the mouse leg).</param>
    /// <param name="keyboardSystems">Hardware keyboard readers with their own seam; nulls ignored.</param>
    public static void Engage(string screen, CursorInputSystem cursor,
        params AKeyboardInputHandlingSystem?[] keyboardSystems)
    {
        SkipHardwareRead = true;
        cursor.SkipHardwareRead = true;

        var pinned = 1;
        foreach (var system in keyboardSystems)
        {
            if (system == null) continue;
            system.SkipHardwareRead = true;
            pinned++;
        }

        Logger.Info($"{EngagedLog} on '{screen}': shared keyboard gate + {pinned} hardware reader(s).");
    }
}
