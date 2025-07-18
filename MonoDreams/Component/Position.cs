using Microsoft.Xna.Framework;
using MonoGame.Extended;

namespace MonoDreams.Component;

public class Position(Vector2 startingPosition = default)
{
    public Vector2 Current = startingPosition;
    public Vector2 Last = startingPosition;
    public Vector2 LastDelta = Vector2.Zero;
    
    public bool HasChanged => Current != Last;
    public Vector2 Delta => Current - Last;
}