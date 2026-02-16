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
}