using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public class PlayerInput
{
    public readonly InputState.InputState Right;
    public readonly InputState.InputState Left;
    public readonly InputState.InputState Jump;
    public Vector2 CursorPosition;
        
    public PlayerInput()
    {
        Right = new InputState.InputState();
        Left = new InputState.InputState();
        Jump = new InputState.InputState();
        CursorPosition = Vector2.Zero;
    }
}