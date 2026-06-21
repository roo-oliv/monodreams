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
    public Func<bool> ShouldSuppressInput { get; set; }

    public virtual void Update(GameState state)
    {
        if (SkipHardwareRead) return;
        if (ShouldSuppressInput?.Invoke() == true) return;

        // Read the hardware once per frame, then OR-aggregate by action: an action is
        // "down" this frame if ANY of its mapped keys is down. Each distinct AInputState is
        // updated exactly once per frame, so multiple keys can drive one action (E OR Enter)
        // without a later mapping overwriting an earlier one. AInputState.Update derives the
        // JustPressed/JustReleased edges from the previous committed state, so a single
        // call per frame with the OR'd value preserves edge detection. One key per action
        // behaves exactly as before.
        var keyboard = Keyboard.GetState();
        var downByAction = new Dictionary<AInputState, bool>();
        foreach (var (inputState, key) in InputMapping)
        {
            var down = keyboard.IsKeyDown(key);
            downByAction[inputState] = downByAction.TryGetValue(inputState, out var prev) ? prev || down : down;
        }

        foreach (var (inputState, down) in downByAction)
        {
            inputState.Update(down, state);
        }
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        InputMapping.Clear();
        GC.SuppressFinalize(this);
    }
}
