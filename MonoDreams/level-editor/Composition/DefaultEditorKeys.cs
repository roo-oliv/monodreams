#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Input;
using MonoDreams.System.Input;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's default keyboard surface for hosts <b>without</b> their own keyboard-action
/// mapping layer (the Demos host; any new host): one hardware-edge system over the editor's
/// <b>tool-contextual</b> keys — PageUp/PageDown (within-band order nudge), Enter (boundary commit),
/// Q/E (palette ghost-rotate) — exposing ready-made <see cref="EditorInputBindings"/> backed by its own
/// per-action edge states. This exists so a new host never has to reinvent keyboard edge detection to
/// compose the <see cref="EditorOverlay"/>; a host that already maps actions (the Examples
/// <c>InputMappingSystem</c>) keeps wiring its own action states instead.
///
/// <para><b>The GLOBAL editor shortcuts are NOT here (UX3-E).</b> Delete, frame-scene, undo, and redo —
/// plus <c>Shift+A</c> — are the consolidated <c>EditorShortcuts</c> chord table read by
/// <c>EditorShortcutSystem</c> through the raw keyboard, so this surface no longer maps
/// <see cref="Keys.Delete"/> / <see cref="Keys.Home"/> or the removed bare <c>Z</c>/<c>Y</c> undo/redo.
/// There is no edit-mode toggle key: play/pause/restart are the toolbar's transport buttons
/// (<see cref="EditorTransport"/>).</para>
///
/// <para>Weave it into the update pipeline before the editor systems that read the bindings
/// (registrar entry name <c>editor.keys</c> by convention, <c>RunNormally</c>) so the frame's
/// edges are fresh when the editor systems read them. It inherits the standard
/// <see cref="AKeyboardInputHandlingSystem"/> seams (<c>SkipHardwareRead</c> /
/// <c>ShouldSuppressInput</c>) like every other keyboard system.</para>
/// </summary>
public sealed class DefaultEditorKeys : AKeyboardInputHandlingSystem
{
    private sealed class KeyState : AInputState;

    private readonly KeyState _orderForward = new();
    private readonly KeyState _orderBack = new();
    private readonly KeyState _commit = new();
    private readonly KeyState _rotateCcw = new();
    private readonly KeyState _rotateCw = new();
    private readonly List<(AInputState inputState, Keys)> _mapping;

    public DefaultEditorKeys()
    {
        // Only the tool-contextual keys. Delete / Home (frame) / Z (undo) / Y (redo) moved to the
        // EditorShortcuts chord table (UX3-E) — the chord system reads them off the raw keyboard.
        _mapping =
        [
            (_orderForward, Keys.PageUp),
            (_orderBack, Keys.PageDown),
            (_commit, Keys.Enter), // the boundary tool's commit
            (_rotateCcw, Keys.Q),  // rotate the armed palette ghost counter-clockwise
            (_rotateCw, Keys.E),   // rotate the armed palette ghost clockwise
        ];
        Bindings = new EditorInputBindings(
            orderForwardRequested: _ => _orderForward.JustPressed(),
            orderBackRequested: _ => _orderBack.JustPressed(),
            commitRequested: _ => _commit.JustPressed(),
            rotateCwRequested: _ => _rotateCw.JustPressed(),
            rotateCcwRequested: _ => _rotateCcw.JustPressed());
    }

    public override List<(AInputState inputState, Keys)> InputMapping => _mapping;

    /// <summary>The ready-made editor input bindings, backed by this system's key states —
    /// pass straight to the <see cref="EditorOverlay"/> constructor.</summary>
    public EditorInputBindings Bindings { get; }
}
