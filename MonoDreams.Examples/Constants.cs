using Microsoft.Xna.Framework;

namespace MonoDreams.Examples;

public static class Constants
{
    public static int WorldGravity = 1600;
    public static float JumpGravityFactor = 0.78f;
    public static int JumpVelocity = -400;
    public static int JumpHVelocity = 300;
    public static int MaxWalkVelocity = 210;
    public static int MaxFallVelocity = 700;
    public static int SlidingVelocity = 120;
    
    public static int ScalingSpeed = 8;
    public static Point PlayerSize { get; } = new(22, 44);
    public static Vector2 PlayerSpriteOffset { get; } = new(-6, -14);
}
