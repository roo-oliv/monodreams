using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public class DynamicBody
{
    public static int WorldGravity = 7200;

    public Vector2 CurrentPosition;
    public Vector2 NextPosition;
    public Vector2 LastPosition;
    public bool IsRiding { get; set; }
    public bool IsJumping { get; set; }
    public int Gravity { get; set; }

    public DynamicBody(float x, float y)
    {
        CurrentPosition = new Vector2(x, y);
        NextPosition = new Vector2(x, y);
        LastPosition = new Vector2(x, y);
        IsRiding = false;
        IsJumping = false;
        Gravity = WorldGravity;
    }
}