#nullable enable
using System;
using Microsoft.Xna.Framework.Input;

namespace MonoDreams.Input;

/// <summary>
/// The modifier keys a <see cref="KeyChord"/> requires. Bitwise, so a chord can require several at
/// once (e.g. <c>Ctrl | Shift</c>). Left/right variants of a physical modifier both count as the same
/// flag (see <see cref="KeyChordTracker"/>).
///
/// <para><see cref="PlatformCommand"/> is the one <b>virtual</b> modifier: it stands for "the OS's
/// primary accelerator key" and is resolved to a concrete modifier at MATCH time from an injected
/// platform flag — <see cref="Meta"/> (⌘) on macOS, <see cref="Ctrl"/> everywhere else — so a binding
/// declares "Cmd on Mac, Ctrl on Windows/Linux" exactly once. The struct itself never reads the OS;
/// resolution happens in <see cref="KeyChord.ResolveModifiers"/>, keeping <see cref="KeyChord"/>
/// platform-blind (no <c>#if</c>, no runtime OS query inside this type).</para>
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3,

    /// <summary>Virtual: resolves to <see cref="Meta"/> on macOS and <see cref="Ctrl"/> elsewhere, at
    /// match time via the injected <c>commandIsMeta</c> flag. Mutually intended with — not additional
    /// to — <see cref="Ctrl"/>/<see cref="Meta"/>: a chord uses PlatformCommand OR a concrete modifier,
    /// never redundantly both.</summary>
    PlatformCommand = 1 << 4,
}

/// <summary>
/// A pure, platform-blind keyboard chord: one <see cref="Keys"/> trigger plus the exact set of
/// <see cref="KeyModifiers"/> that must be held for it to fire. Immutable value type. It carries the
/// virtual <see cref="KeyModifiers.PlatformCommand"/> modifier unresolved; <see cref="ResolveModifiers"/>
/// turns it into a concrete modifier set given the platform flag, so the struct never queries the OS.
///
/// <para>The firing rule (exact-modifier matching, extra-non-modifier-keys-allowed) lives in
/// <see cref="KeyChordTracker"/> — this type is only the declaration. Game-agnostic: it lives in
/// <c>foundation</c> so any feature (not just the editor) can bind combo inputs.</para>
/// </summary>
public readonly struct KeyChord : IEquatable<KeyChord>
{
    /// <summary>The trigger key — the chord fires on this key's press edge.</summary>
    public Keys Key { get; }

    /// <summary>The modifiers that must be held (exactly) when <see cref="Key"/> is pressed.</summary>
    public KeyModifiers Modifiers { get; }

    public KeyChord(Keys key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>
    /// Resolves the virtual <see cref="KeyModifiers.PlatformCommand"/> into a concrete modifier —
    /// <see cref="KeyModifiers.Meta"/> when <paramref name="commandIsMeta"/> (macOS), else
    /// <see cref="KeyModifiers.Ctrl"/> — and returns the concrete required-modifier set. A chord with no
    /// PlatformCommand is returned unchanged (idempotent).
    /// </summary>
    public KeyModifiers ResolveModifiers(bool commandIsMeta)
    {
        var m = Modifiers;
        if ((m & KeyModifiers.PlatformCommand) == 0) return m;
        m &= ~KeyModifiers.PlatformCommand;
        m |= commandIsMeta ? KeyModifiers.Meta : KeyModifiers.Ctrl;
        return m;
    }

    public bool Equals(KeyChord other) => Key == other.Key && Modifiers == other.Modifiers;
    public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Key, (int)Modifiers);
    public override string ToString() => $"{Modifiers}+{Key}";
}
