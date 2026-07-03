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

    /// <summary>Deletes the current editor selection (Edit mode only).</summary>
    public static readonly InputState Delete = new();
    /// <summary>Undo the last editor command (Edit mode only).</summary>
    public static readonly InputState Undo = new();
    /// <summary>Redo the last undone editor command (Edit mode only).</summary>
    public static readonly InputState Redo = new();
    /// <summary>Frames the editor camera on all renderable content — centre + zoom-fit (Edit mode only).</summary>
    public static readonly InputState Frame = new();
}