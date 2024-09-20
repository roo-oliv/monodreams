using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Physics;

public class Velocity(Vector2 initialVelocity = default)
{
    public Vector2 Last = Vector2.Zero;
    public Vector2 Current = initialVelocity;
    
    public bool HasChanged => Current != Last;
    public Vector2 Delta => Current - Last;
}