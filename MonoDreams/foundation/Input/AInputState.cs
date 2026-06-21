using MonoDreams.State;

namespace MonoDreams.Input;

/// <summary>
/// Per-action input state with frame-edge detection.
///
/// The input pipeline (<see cref="MonoDreams.System.Input.AKeyboardInputHandlingSystem"/>
/// or <see cref="MonoDreams.System.Input.InputReplaySystem"/>) calls <see cref="Update"/>
/// once per frame. Game systems then read <see cref="Pressed"/>, <see cref="JustPressed"/>,
/// or <see cref="JustReleased"/> to drive behaviour. A system that handles an edge can
/// call <see cref="Consume"/> to hide it from systems that run later in the same frame.
/// </summary>
public abstract class AInputState(float buffer = 0)
{
    private float _lastReleaseTime = float.MinValue;
    private bool _pressed;
    private bool _wasPressedLastFrame;
    private bool _consumed;

    public void Update(bool pressed, GameState gameState)
    {
        _wasPressedLastFrame = _pressed;
        _consumed = false;
        if (pressed == _pressed) return;
        if (!pressed) _lastReleaseTime = gameState.TotalTime;
        _pressed = pressed;
    }

    /// <summary>True on the frame the input transitioned from released to pressed.</summary>
    public bool JustPressed() => !_consumed && _pressed && !_wasPressedLastFrame;

    /// <summary>True on the frame the input transitioned from pressed to released.</summary>
    public bool JustReleased() => !_consumed && !_pressed && _wasPressedLastFrame;

    /// <summary>
    /// True if currently pressed, or recently released within the input-forgiveness
    /// <c>buffer</c> window (set via the constructor).
    /// </summary>
    public bool Pressed(GameState gameState)
    {
        if (_pressed) return true;
        if (buffer <= 0f) return false;
        var delta = gameState.TotalTime - _lastReleaseTime;
        return delta >= 0 && delta <= buffer;
    }

    /// <summary>
    /// Mark this input as consumed for the rest of the current frame. Subsequent
    /// <see cref="JustPressed"/> / <see cref="JustReleased"/> calls return <c>false</c>
    /// until the next <see cref="Update"/>. <see cref="Pressed"/> is unaffected — the
    /// input is still physically held.
    /// </summary>
    public void Consume() => _consumed = true;
}
