using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Physics;

public class Position(Vector2 startingPosition = default)
{
    public Vector2 Current = startingPosition;
    public Vector2 Last = startingPosition;
    
    public bool HasChanged => Current != Last;
    public Vector2 Delta => Current - Last;
}