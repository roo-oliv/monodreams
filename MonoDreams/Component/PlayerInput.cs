namespace MonoDreams.Component;

public class PlayerInput
{
    public readonly InputState.InputState Right;
    public readonly InputState.InputState Left;
    public readonly InputState.InputState Jump;
        
    public PlayerInput()
    {
        Right = new InputState.InputState();
        Left = new InputState.InputState();
        Jump = new InputState.InputState();
    }
}