using Microsoft.Xna.Framework.Input;
using MonoDreams.Examples.Input;
using MonoDreams.Input;
using MonoDreams.System;
using MonoDreams.System.Input;

namespace MonoDreams.Examples.System;

public class InputHandlingSystem : AKeyboardInputHandlingSystem
{
    public override List<(AInputState inputState, Keys)> InputMapping =>
    [
        (InputState.Up, Keys.W),
        (InputState.Down, Keys.S),
        (InputState.Left, Keys.A),
        (InputState.Right, Keys.D),
        (InputState.Jump, Keys.Space),
        (InputState.Grab, Keys.LeftShift),
        (InputState.Grab, Keys.RightShift),
        (InputState.Grab, Keys.K),
        (InputState.Exit, Keys.Escape)
    ];
}