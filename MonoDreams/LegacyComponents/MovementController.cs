using Microsoft.Xna.Framework;

namespace MonoDreams.LegacyComponents;

public class MovementController
{
    public Vector2 Velocity;

    public MovementController()
    {
        Velocity = Vector2.Zero;
    }

    public void Clear()
    {
        Velocity = Vector2.Zero;
    }
}