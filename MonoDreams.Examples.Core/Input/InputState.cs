using MonoDreams.Input;

namespace MonoDreams.Examples.Input;

public class InputState : AInputState
{
    public static readonly InputState Up = new();
    public static readonly InputState Down = new();
    public static readonly InputState Left = new();
    public static readonly InputState Right = new();
    public static readonly InputState Jump = new();
    public static readonly InputState Grab = new();
    public static readonly InputState Exit = new();
    public static readonly InputState Orb = new();
    public static readonly InputState Interact = new();

    // Delete / Undo / Redo / Frame moved to the editor's EditorShortcuts chord table (UX3-E): they are
    // read off the raw keyboard by EditorShortcutSystem (Delete / Home / Cmd+Z / Cmd+Shift+Z), not as
    // game-mapped actions — so the bare Z/Y undo/redo are gone (Blender parity: bare keys are tools).
    /// <summary>UI ordinal navigation, forward — Tab. Consumed by <c>UIFocusSystem</c> (the menu's
    /// keyboard navigation); inert on screens that register no focus system.</summary>
    public static readonly InputState MenuNext = new();

    /// <summary>UI ordinal navigation, backward — Shift-Tab. A CHORD, so it is driven from
    /// <c>InputMappingSystem.Update</c> rather than from the (action, key) table, which ORs single
    /// keys per action and cannot express a modifier.</summary>
    public static readonly InputState MenuPrevious = new();

    /// <summary>Rotates the armed palette ghost clockwise before stamping (Edit mode only).</summary>
    public static readonly InputState RotateCw = new();
    /// <summary>Rotates the armed palette ghost counter-clockwise before stamping (Edit mode only).</summary>
    public static readonly InputState RotateCcw = new();
}