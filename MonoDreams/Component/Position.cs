using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public class Position
{
    public Vector2 Current;
    public Vector2 Next;
    public Vector2 Last;

    public Position(Vector2 startingPosition)
    {
        Current = startingPosition;
        Next = startingPosition;
        Last = startingPosition;
    }
}