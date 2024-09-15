using MonoDreams.State;

namespace MonoDreams.Input;

public abstract class AInputState(float buffer = 0)
{
    private float _lastPressTime = float.MinValue;
    private float _lastReleaseTime = float.MinValue;
    private bool _pressed;

    public void Update(bool pressed, GameState gameState)
    {
        if (pressed == _pressed) return;
        if (_pressed) _lastPressTime = gameState.TotalTime;
        else _lastReleaseTime = gameState.TotalTime;
        _pressed = pressed;
    }

    public bool JustPressed(GameState gameState) =>
        (_pressed && WithinBuffer(gameState.TotalTime - _lastPressTime)) ||
        (!_pressed && WithinBuffer(gameState.TotalTime - _lastReleaseTime));

    public bool JustReleased(GameState gameState) =>
        (!_pressed && WithinBuffer(gameState.TotalTime - _lastPressTime)) ||
        (_pressed && WithinBuffer(gameState.TotalTime - _lastReleaseTime));

    public bool Pressed(GameState gameState) => _pressed || JustReleased(gameState);

    // delta >= 0 && delta <= buffer
    private bool WithinBuffer(float delta) => delta * (buffer - delta) >= 0;
}