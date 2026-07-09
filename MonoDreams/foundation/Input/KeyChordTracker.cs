#nullable enable
using System;
using Microsoft.Xna.Framework.Input;

namespace MonoDreams.Input;

/// <summary>
/// Detects <see cref="KeyChord"/> press edges across frames. Game-agnostic (<c>foundation</c>): any
/// feature can hold one to drive combo inputs, not just the editor.
///
/// <para><b>Firing rule (the whole contract).</b> A chord fires on the <b>press edge</b> of its
/// <see cref="KeyChord.Key"/> — down this frame, up last frame — while <b>exactly</b> the chord's
/// required modifiers are held. "Exactly" is the load-bearing word: extra held <i>non-modifier</i> keys
/// do NOT block a match (you can hold anything else), but extra held <i>modifiers</i> DO
/// (<c>Ctrl+Shift+Z</c> must not also fire <c>Ctrl+Z</c>). Left and right variants of a modifier both
/// count. The virtual <see cref="KeyModifiers.PlatformCommand"/> is resolved per the injected
/// <c>commandIsMeta</c> flag — Meta (⌘) on macOS, Ctrl elsewhere — so the same table works on both.</para>
///
/// <para><b>Seam.</b> Keyboard state arrives through the same injectable <c>Func&lt;KeyboardState&gt;</c>
/// idiom the editor dialog uses (default <see cref="Keyboard.GetState"/>), so a headless/test driver
/// supplies scripted states. The pure edge math is <see cref="Matches"/> — hand-built
/// <see cref="KeyboardState"/> pairs test it with no seam, no game, no window.</para>
///
/// <para><b>Replay caveat.</b> The engine's input-replay channel synthesizes <c>AInputState</c> actions,
/// not raw keyboard chords, so chord-driven features are NOT exercised through replay — they are tested
/// through their own op channels (the editor's <c>menu:*</c> / toolbar ops) and, for the matching itself,
/// through <see cref="Matches"/>. A future replay-v2 recording raw keyboard is the named terrain that
/// would make chords replayable.</para>
/// </summary>
public sealed class KeyChordTracker
{
    private readonly Func<KeyboardState> _getKeyboardState;
    private bool _primed;

    /// <summary>Resolve <see cref="KeyModifiers.PlatformCommand"/> to <see cref="KeyModifiers.Meta"/>
    /// (macOS) rather than <see cref="KeyModifiers.Ctrl"/>. Injected — the module never reads the OS.</summary>
    public bool CommandIsMeta { get; }

    /// <summary>The keyboard state sampled the frame before <see cref="Current"/> (the edge baseline).</summary>
    public KeyboardState Previous { get; private set; }

    /// <summary>The keyboard state sampled by the most recent <see cref="Update"/>.</summary>
    public KeyboardState Current { get; private set; }

    /// <param name="commandIsMeta">Whether <see cref="KeyModifiers.PlatformCommand"/> means ⌘/Meta
    /// (macOS) — injected by the composing layer, never read from the OS here.</param>
    /// <param name="getKeyboardState">The keyboard seam (default <see cref="Keyboard.GetState"/>).</param>
    public KeyChordTracker(bool commandIsMeta, Func<KeyboardState>? getKeyboardState = null)
    {
        CommandIsMeta = commandIsMeta;
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
    }

    /// <summary>
    /// Advances one frame: shifts <see cref="Current"/> → <see cref="Previous"/> and re-samples the
    /// keyboard. Call once per frame BEFORE querying <see cref="Pressed"/>. The first call primes both
    /// buffers to the same sample so a key already held at startup is not reported as a fresh press.
    /// </summary>
    public void Update()
    {
        if (!_primed)
        {
            Previous = Current = _getKeyboardState();
            _primed = true;
            return;
        }
        Previous = Current;
        Current = _getKeyboardState();
    }

    /// <summary>Whether <paramref name="chord"/> fired between <see cref="Previous"/> and
    /// <see cref="Current"/> (see the firing rule on the class).</summary>
    public bool Pressed(KeyChord chord) => Matches(chord, Previous, Current, CommandIsMeta);

    /// <summary>
    /// The pure edge match. Fires on <paramref name="chord"/>'s press edge (down in
    /// <paramref name="current"/>, up in <paramref name="previous"/>) while EXACTLY the required
    /// modifiers — after resolving <see cref="KeyModifiers.PlatformCommand"/> via
    /// <paramref name="commandIsMeta"/> — are held. Extra non-modifier keys are ignored; extra modifiers
    /// block. No seam, no state — directly testable with hand-built <see cref="KeyboardState"/>s.
    /// </summary>
    public static bool Matches(KeyChord chord, KeyboardState previous, KeyboardState current, bool commandIsMeta)
    {
        // Press edge on the trigger key.
        if (!current.IsKeyDown(chord.Key) || previous.IsKeyDown(chord.Key)) return false;
        // Exactly the required modifier set — no more, no fewer.
        return HeldModifiers(current) == chord.ResolveModifiers(commandIsMeta);
    }

    /// <summary>The concrete modifier set currently held (left/right variants collapse to one flag).</summary>
    private static KeyModifiers HeldModifiers(KeyboardState keys)
    {
        var m = KeyModifiers.None;
        if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl)) m |= KeyModifiers.Ctrl;
        if (keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift)) m |= KeyModifiers.Shift;
        if (keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt)) m |= KeyModifiers.Alt;
        // The macOS ⌘ key surfaces as the GUI/"Windows" key through SDL/DesktopGL and KNI.
        if (keys.IsKeyDown(Keys.LeftWindows) || keys.IsKeyDown(Keys.RightWindows)) m |= KeyModifiers.Meta;
        return m;
    }
}
