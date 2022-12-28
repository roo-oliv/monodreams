using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public struct Position
{
    public Vector2 CurrentValue;
    public Vector2 NextValue;
    public Vector2 LastValue;

    public Position(float x, float y)
    {
        CurrentValue = new Vector2(x, y);
        NextValue = new Vector2(x, y);
        LastValue = new Vector2(x, y);
    }

    public void UpdateValue(Vector2 newPosition)
    {
        LastValue = CurrentValue;
        CurrentValue = newPosition;
    }

    public void ArtificialIncrement(float x, float y)
    {
        LastValue.X += x;
        LastValue.Y += y;
        CurrentValue.X += x;
        CurrentValue.Y += y;
    }
}