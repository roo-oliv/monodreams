using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public struct Collidable
{
    public bool Passive;
    public Rectangle Bounds;

    public Collidable(Rectangle bounds, bool passive = true)
    {
        Passive = passive;
        Bounds = bounds;
    }
}