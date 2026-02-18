namespace MonoDreams.Component;

public class CameraFollowTarget
{
    public float DampingX { get; set; } = 5.0f;
    public float DampingY { get; set; } = 5.0f;
    public float MaxDistanceX { get; set; } = 100.0f;
    public float MaxDistanceY { get; set; } = 100.0f;
    public bool IsActive { get; set; } = true;
}
