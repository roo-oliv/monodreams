#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Input;

namespace MonoDreams.LevelEditor.Input;

/// <summary>The editor action a <see cref="KeyChord"/> triggers. One entry per bound shortcut; the
/// overlay maps each to the SAME shared editor instance the toolbar/menu use (never a second path).</summary>
public enum EditorShortcutAction
{
    /// <summary>Undo the last edit (the shared <c>EditorHistory</c>).</summary>
    Undo,

    /// <summary>Redo the last undone edit (the shared <c>EditorHistory</c>).</summary>
    Redo,

    /// <summary>Delete the current selection (the snapshotting delete on the shared command system).</summary>
    Delete,

    /// <summary>Frame the editor VIEW on all renderable content (the shared camera-nav frame math).</summary>
    FrameScene,

    /// <summary>Open the <b>Add</b> menu at the cursor (the Entities-panel add section, via the shared
    /// context-menu coordinator).</summary>
    AddMenu,

    /// <summary>Enter the Blender-style <b>grab</b> modal transform over the selection (UX3-F).</summary>
    ModalGrab,

    /// <summary>Enter the <b>scale</b> modal transform (UX3-F; rig ⇒ zoom).</summary>
    ModalScale,

    /// <summary>Enter the <b>rotate</b> modal transform (UX3-F; refused for the rig).</summary>
    ModalRotate,
}

/// <summary>
/// The ONE editor keyboard-shortcut table (UX3-E design §4): chord → editor action, as data plus a
/// pure <see cref="Match(KeyboardState, KeyboardState, bool)"/>. Replaces the scattered per-action
/// keyboard-edge predicates so there is a single place to read the bindings. Game-agnostic — the chords
/// are editor-standard (Blender parity), not a game's remappable keys, so they live here and read raw
/// keyboard through <see cref="MonoDreams.LevelEditor.System.EditorShortcutSystem"/>, not through a
/// game's action mapping.
///
/// <para><b>Blender parity.</b> Bare letter keys are reserved for tools, so Undo/Redo are CHORDS
/// (<c>Cmd/Ctrl+Z</c> / <c>Cmd/Ctrl+Shift+Z</c>) — the pre-existing bare <c>Z</c>/<c>Y</c> undo/redo
/// were removed. <see cref="Keys.Delete"/> and <see cref="Keys.Home"/> (frame) stay bare — they are not
/// tool letters. <c>Shift+A</c> opens the Add menu. The table is the single point to add G/S/R and
/// friends in a later wave.</para>
/// </summary>
public sealed class EditorShortcuts
{
    // The binding table. Order is not load-bearing: matching is EXACT-modifier, so at most one chord
    // matches a given (key, held-modifiers) pair — Cmd+Shift+Z resolves to Redo without also hitting
    // the Cmd+Z Undo binding (pre-mortem #3). The more-specific chord is listed first for readability.
    private static readonly IReadOnlyList<(KeyChord Chord, EditorShortcutAction Action)> Table = new[]
    {
        (new KeyChord(Keys.Z, KeyModifiers.PlatformCommand | KeyModifiers.Shift), EditorShortcutAction.Redo),
        (new KeyChord(Keys.Z, KeyModifiers.PlatformCommand), EditorShortcutAction.Undo),
        (new KeyChord(Keys.A, KeyModifiers.Shift), EditorShortcutAction.AddMenu),
        (new KeyChord(Keys.Delete), EditorShortcutAction.Delete),
        (new KeyChord(Keys.Home), EditorShortcutAction.FrameScene),
        // UX3-F: the bare G/S/R modal transforms — Blender parity (bare letter keys ARE the tools).
        // They enter modal mode via the shortcut dispatch; the modal then owns the keyboard, and the
        // shortcut gate's ModalActive flag stops them re-triggering mid-modal.
        (new KeyChord(Keys.G), EditorShortcutAction.ModalGrab),
        (new KeyChord(Keys.S), EditorShortcutAction.ModalScale),
        (new KeyChord(Keys.R), EditorShortcutAction.ModalRotate),
    };

    /// <summary>The bound chords + their actions (read-only). Exposed for tests and inspection.</summary>
    public IReadOnlyList<(KeyChord Chord, EditorShortcutAction Action)> Bindings => Table;

    /// <summary>
    /// The pure match: the first action whose chord fires between <paramref name="previous"/> and
    /// <paramref name="current"/> (press edge + exactly its modifiers, resolving PlatformCommand via
    /// <paramref name="commandIsMeta"/>), or null. Hand-built <see cref="KeyboardState"/>s test it.
    /// </summary>
    public EditorShortcutAction? Match(KeyboardState previous, KeyboardState current, bool commandIsMeta)
    {
        foreach (var (chord, action) in Table)
            if (KeyChordTracker.Matches(chord, previous, current, commandIsMeta))
                return action;
        return null;
    }

    /// <summary>Convenience over a live <see cref="KeyChordTracker"/> (the system path).</summary>
    public EditorShortcutAction? Match(KeyChordTracker tracker) =>
        Match(tracker.Previous, tracker.Current, tracker.CommandIsMeta);
}
