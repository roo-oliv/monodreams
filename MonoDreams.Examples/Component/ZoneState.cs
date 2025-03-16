using SharpDX;

namespace MonoDreams.Examples.Component;

public class ZoneState(Action<ZoneState> callback)
{
    private bool _isActive;
    public bool Enabled { get; set; } = true;
    public int ActivationCount { get; private set; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            ActivationCount++;
            _isActive = value;
            callback(this);
        }
    }
}