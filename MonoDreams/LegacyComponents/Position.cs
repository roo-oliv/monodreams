using Microsoft.Xna.Framework;

namespace MonoDreams.LegacyComponents;

public struct Position
{
    public Vector2 CurrentLocation;
    public Vector2 NextLocation;
    public Vector2 LastLocation;
    public Orientation CurrentOrientation;
    public Orientation NextOrientation;
    public Orientation LastOrientation;

    public Position(Vector2 startingLocation, Orientation startingOrientation = Orientation.Right)
    {
        CurrentLocation = startingLocation;
        NextLocation = startingLocation;
        LastLocation = startingLocation;
        CurrentOrientation = startingOrientation;
        NextOrientation = startingOrientation;
        LastOrientation = startingOrientation;
    }
    
    public bool HasUpdates => CurrentLocation != NextLocation || LastLocation != CurrentLocation || CurrentOrientation != NextOrientation || LastOrientation != CurrentOrientation;
}

public enum Orientation
{
    Up = -1,
    Down = 1,
    Left = -1,
    Right = 1
}
