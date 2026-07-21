using Microsoft.Xna.Framework.Input;
using MonoDreams.Input;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the foundation chord layer (UX3-E): <see cref="KeyChord.ResolveModifiers"/> (the
/// PlatformCommand → Meta/Ctrl injection) and <see cref="KeyChordTracker.Matches"/> (the exact-modifier,
/// press-edge firing rule — pre-mortem #3 over-matching and #7 macOS-vs-Ctrl, both resolutions). Pure:
/// hand-built <see cref="KeyboardState"/> pairs, no seam, no window. The stateful tracker's frame-to-frame
/// priming + edge is exercised through the injectable <c>Func&lt;KeyboardState&gt;</c> seam.
/// </summary>
public class KeyChordTests
{
    private static KeyboardState Held(params Keys[] keys) => new(keys);
    private static KeyboardState None() => new();

    // ─── KeyChord.ResolveModifiers (the PlatformCommand injection) ────────────────────────────────

    [Fact]
    public void ResolveModifiers_PlatformCommand_BecomesMetaOnMac_CtrlElsewhere()
    {
        var chord = new KeyChord(Keys.Z, KeyModifiers.PlatformCommand);
        Assert.Equal(KeyModifiers.Meta, chord.ResolveModifiers(commandIsMeta: true));
        Assert.Equal(KeyModifiers.Ctrl, chord.ResolveModifiers(commandIsMeta: false));
    }

    [Fact]
    public void ResolveModifiers_KeepsOtherModifiers_AndIsIdempotentWithoutPlatformCommand()
    {
        var withShift = new KeyChord(Keys.Z, KeyModifiers.PlatformCommand | KeyModifiers.Shift);
        Assert.Equal(KeyModifiers.Meta | KeyModifiers.Shift, withShift.ResolveModifiers(commandIsMeta: true));
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Shift, withShift.ResolveModifiers(commandIsMeta: false));

        var plain = new KeyChord(Keys.Delete, KeyModifiers.None);
        Assert.Equal(KeyModifiers.None, plain.ResolveModifiers(commandIsMeta: true));
        var shiftOnly = new KeyChord(Keys.A, KeyModifiers.Shift);
        Assert.Equal(KeyModifiers.Shift, shiftOnly.ResolveModifiers(commandIsMeta: false));
    }

    // ─── press edge, not level ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Matches_FiresOnlyOnThePressEdge_NotWhileHeld()
    {
        var chord = new KeyChord(Keys.Z, KeyModifiers.Ctrl);
        // Up → down = fire.
        Assert.True(KeyChordTracker.Matches(chord, Held(Keys.LeftControl), Held(Keys.LeftControl, Keys.Z), false));
        // Down → down (still held) = no fire (edge, not level).
        Assert.False(KeyChordTracker.Matches(chord, Held(Keys.LeftControl, Keys.Z), Held(Keys.LeftControl, Keys.Z), false));
    }

    // ─── exact-modifier rule: pre-mortem #3 (Ctrl+Shift+Z must NOT also fire Ctrl+Z) ────────────────

    [Fact]
    public void Matches_ExtraHeldModifier_Blocks_SoCtrlShiftZ_DoesNotFireCtrlZ()
    {
        var ctrlZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl);
        var ctrlShiftZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl | KeyModifiers.Shift);
        var prev = Held(Keys.LeftControl, Keys.LeftShift);
        var cur = Held(Keys.LeftControl, Keys.LeftShift, Keys.Z);

        Assert.False(KeyChordTracker.Matches(ctrlZ, prev, cur, false));      // superset held → no fire
        Assert.True(KeyChordTracker.Matches(ctrlShiftZ, prev, cur, false));  // exact → fires
    }

    [Fact]
    public void Matches_MissingRequiredModifier_Blocks_SoCtrlZ_DoesNotFireCtrlShiftZ()
    {
        var ctrlShiftZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl | KeyModifiers.Shift);
        var prev = Held(Keys.LeftControl);
        var cur = Held(Keys.LeftControl, Keys.Z); // Shift NOT held → subset

        Assert.False(KeyChordTracker.Matches(ctrlShiftZ, prev, cur, false));
    }

    [Fact]
    public void Matches_ExtraHeldNonModifierKey_DoesNotBlock()
    {
        var ctrlZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl);
        // X (a non-modifier) is held throughout; the Ctrl+Z edge still fires.
        var prev = Held(Keys.LeftControl, Keys.X);
        var cur = Held(Keys.LeftControl, Keys.X, Keys.Z);
        Assert.True(KeyChordTracker.Matches(ctrlZ, prev, cur, false));
    }

    [Fact]
    public void Matches_BareChord_RequiresNoModifiers_SoShiftDeleteDoesNotFireBareDelete()
    {
        var bareDelete = new KeyChord(Keys.Delete);
        Assert.True(KeyChordTracker.Matches(bareDelete, None(), Held(Keys.Delete), false));
        // A held modifier makes it a different chord → the bare binding must not fire.
        Assert.False(KeyChordTracker.Matches(bareDelete, Held(Keys.LeftShift), Held(Keys.LeftShift, Keys.Delete), false));
    }

    // ─── left/right modifier variants both count ────────────────────────────────────────────────────

    [Fact]
    public void Matches_LeftAndRightModifierVariants_BothCount()
    {
        var ctrlZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl);
        Assert.True(KeyChordTracker.Matches(ctrlZ, Held(Keys.RightControl), Held(Keys.RightControl, Keys.Z), false));

        var shiftA = new KeyChord(Keys.A, KeyModifiers.Shift);
        Assert.True(KeyChordTracker.Matches(shiftA, Held(Keys.RightShift), Held(Keys.RightShift, Keys.A), false));
    }

    // ─── PlatformCommand: pre-mortem #7 — both macOS (⌘/Meta) and Windows/Linux (Ctrl) ──────────────

    [Fact]
    public void Matches_PlatformCommand_ResolvesToMeta_OnMac()
    {
        var cmdZ = new KeyChord(Keys.Z, KeyModifiers.PlatformCommand);
        var withMeta = (None(), Held(Keys.LeftWindows, Keys.Z));
        var withCtrl = (None(), Held(Keys.LeftControl, Keys.Z));

        // commandIsMeta = true (macOS): the ⌘/Meta key fires it; the Ctrl key does NOT.
        Assert.True(KeyChordTracker.Matches(cmdZ, withMeta.Item1, withMeta.Item2, commandIsMeta: true));
        Assert.False(KeyChordTracker.Matches(cmdZ, withCtrl.Item1, withCtrl.Item2, commandIsMeta: true));
    }

    [Fact]
    public void Matches_PlatformCommand_ResolvesToCtrl_Elsewhere()
    {
        var cmdZ = new KeyChord(Keys.Z, KeyModifiers.PlatformCommand);
        var withMeta = (None(), Held(Keys.LeftWindows, Keys.Z));
        var withCtrl = (None(), Held(Keys.LeftControl, Keys.Z));

        // commandIsMeta = false (Windows/Linux): the Ctrl key fires it; the Meta key does NOT.
        Assert.True(KeyChordTracker.Matches(cmdZ, withCtrl.Item1, withCtrl.Item2, commandIsMeta: false));
        Assert.False(KeyChordTracker.Matches(cmdZ, withMeta.Item1, withMeta.Item2, commandIsMeta: false));
    }

    [Fact]
    public void Matches_PlatformCommandPlusShift_BothResolutions()
    {
        var redo = new KeyChord(Keys.Z, KeyModifiers.PlatformCommand | KeyModifiers.Shift);
        // macOS: ⌘+Shift+Z.
        Assert.True(KeyChordTracker.Matches(redo, Held(Keys.LeftWindows, Keys.LeftShift),
            Held(Keys.LeftWindows, Keys.LeftShift, Keys.Z), commandIsMeta: true));
        // Windows/Linux: Ctrl+Shift+Z.
        Assert.True(KeyChordTracker.Matches(redo, Held(Keys.LeftControl, Keys.LeftShift),
            Held(Keys.LeftControl, Keys.LeftShift, Keys.Z), commandIsMeta: false));
    }

    // ─── the stateful tracker: seam + priming ──────────────────────────────────────────────────────

    [Fact]
    public void Tracker_PrimesOnFirstUpdate_SoAHeldKeyIsNotReportedAsAFreshPress()
    {
        // A key held from the very start must not fire on frame 1 (priming), nor on frame 2 (still held).
        var kb = new[] { Held(Keys.LeftControl, Keys.Z) };
        var tracker = new KeyChordTracker(commandIsMeta: false, () => kb[0]);
        var ctrlZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl);

        tracker.Update();
        Assert.False(tracker.Pressed(ctrlZ)); // primed: prev == cur, no edge
        tracker.Update();
        Assert.False(tracker.Pressed(ctrlZ)); // still held, no edge
    }

    [Fact]
    public void Tracker_FiresOnTheFrameTheChordIsPressed_ThenNotWhileHeld()
    {
        var kb = new[] { None() };
        var tracker = new KeyChordTracker(commandIsMeta: false, () => kb[0]);
        var ctrlZ = new KeyChord(Keys.Z, KeyModifiers.Ctrl);

        tracker.Update();                    // prime on empty
        kb[0] = Held(Keys.LeftControl, Keys.Z);
        tracker.Update();                    // press edge
        Assert.True(tracker.Pressed(ctrlZ));

        tracker.Update();                    // same keys still held → no edge
        Assert.False(tracker.Pressed(ctrlZ));
    }
}
