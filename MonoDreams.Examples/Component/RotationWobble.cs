namespace MonoDreams.Examples.Component;

/// <summary>
/// Component for entities that snap between a base rotation and a rotated state.
/// </summary>
public class RotationWobble
{
    public float BaseRotation { get; set; }
    public float AmplitudeRadians { get; set; }
    public float CycleDuration { get; set; } = 0.5f;
}
