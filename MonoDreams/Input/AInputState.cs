namespace MonoDreams.Input;

public abstract class AInputState(float buffer = 0)
{
    private float _lastPressTime = float.MinValue;
    private float _lastReleaseTime = float.MinValue;
    private bool _pressed;

    public void Update(bool pressed, float totalTime)
    {
        if (pressed == _pressed) return;
        if (_pressed) _lastPressTime = totalTime;
        else _lastReleaseTime = totalTime;
        _pressed = pressed;
    }

    public bool JustPressed(float totalTime) =>
        (_pressed && WithinBuffer(totalTime - _lastPressTime)) ||
        (!_pressed && WithinBuffer(totalTime - _lastReleaseTime));

    public bool JustReleased(float totalTime) =>
        (!_pressed && WithinBuffer(totalTime - _lastPressTime)) ||
        (_pressed && WithinBuffer(totalTime - _lastReleaseTime));

    public bool Pressed(float totalTime) => _pressed || JustReleased(totalTime);

    // delta >= 0 && delta <= buffer
    private bool WithinBuffer(float delta) => delta * (buffer - delta) >= 0;
}