namespace MonoDreams.Examples.Component;

/// <summary>
/// Component for entities that snap between a base rotation and an offset state.
/// </summary>
public class StopMotionEffect
{
    public float BaseRotation { get; set; }
    public float OffsetRadians { get; set; }
    public float CycleDuration { get; set; } = 0.5f;
}
