namespace MonoDreams.Component;

public class DynamicBody
{
    public static int WorldGravity = 4900;
    public bool IsRiding;
    public bool IsJumping;
    public int Gravity;

    public DynamicBody()
    {
        IsRiding = false;
        IsJumping = false;
        Gravity = WorldGravity;
    }
}