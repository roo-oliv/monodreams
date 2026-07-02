#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Input;
using MonoDreams.System.Input;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's default keyboard surface for hosts <b>without</b> their own keyboard-action
/// mapping layer (the Demos host; any new host): one hardware-edge system over the standard
/// editor keys — F1 (Play↔Edit toggle), Delete (delete selection), Z (undo), Y (redo),
/// Home (frame scene) — exposing ready-made <see cref="EditorInputBindings"/> backed by its own
/// per-action edge states. This exists so a new host never has to reinvent keyboard edge
/// detection to compose the <see cref="EditorOverlay"/>; a host that already maps actions
/// (the Examples <c>InputMappingSystem</c>) keeps wiring its own action states instead.
///
/// <para>Weave it into the update pipeline <b>before</b> <see cref="EditorOverlay.ModeToggle"/>
/// (registrar entry name <c>editor.keys</c> by convention, <c>RunNormally</c> — F1 must fire in
/// both modes) so the frame's edges are fresh when the editor systems read them. It inherits the
/// standard <see cref="AKeyboardInputHandlingSystem"/> seams (<c>SkipHardwareRead</c> /
/// <c>ShouldSuppressInput</c>) like every other keyboard system.</para>
/// </summary>
public sealed class DefaultEditorKeys : AKeyboardInputHandlingSystem
{
    private sealed class KeyState : AInputState;

    private readonly KeyState _toggleEdit = new();
    private readonly KeyState _delete = new();
    private readonly KeyState _undo = new();
    private readonly KeyState _redo = new();
    private readonly KeyState _frame = new();
    private readonly List<(AInputState inputState, Keys)> _mapping;

    public DefaultEditorKeys()
    {
        _mapping =
        [
            (_toggleEdit, Keys.F1),
            (_delete, Keys.Delete),
            (_undo, Keys.Z),
            (_redo, Keys.Y),
            (_frame, Keys.Home),
        ];
        Bindings = new EditorInputBindings(
            toggleEditRequested: _ => _toggleEdit.JustPressed(),
            deleteRequested: _ => _delete.JustPressed(),
            undoRequested: _ => _undo.JustPressed(),
            redoRequested: _ => _redo.JustPressed(),
            frameRequested: _ => _frame.JustPressed());
    }

    public override List<(AInputState inputState, Keys)> InputMapping => _mapping;

    /// <summary>The ready-made editor input bindings, backed by this system's key states —
    /// pass straight to the <see cref="EditorOverlay"/> constructor.</summary>
    public EditorInputBindings Bindings { get; }
}
