namespace MonoDreams.Examples.Component;

/// <summary>
/// Defines the collision layers used in the game
/// -1 is a special layer that collides with all other layers
/// </summary>
public static class CollisionLayer
{
    public const int Default = 0;
    public const int Player = 1;
    public const int Enemy = 2;
    public const int Projectile = 3;
    public const int Item = 4;
    public const int Dialogue = 5;
} 