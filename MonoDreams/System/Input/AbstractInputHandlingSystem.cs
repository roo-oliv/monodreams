using System;
using System.Collections.Generic;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Input;
using MonoDreams.State;

namespace MonoDreams.System.Input;

public abstract class AKeyboardInputHandlingSystem : ISystem<GameState>
{
    public abstract List<(AInputState inputState, Keys)> InputMapping { get; }

    public bool SkipHardwareRead { get; set; }

    public virtual void Update(GameState state)
    {
        if (SkipHardwareRead) return;

        foreach (var (inputState, key) in InputMapping)
        {
            inputState.Update(Keyboard.GetState().IsKeyDown(key), state);
        }
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        InputMapping.Clear();
        GC.SuppressFinalize(this);
    }
}
