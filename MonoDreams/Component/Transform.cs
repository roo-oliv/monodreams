using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public class Transform(Vector2 startingPosition = default, float startingRotation = 0f, Vector2 origin = default)
{
    public Vector2 CurrentPosition = startingPosition;
    public Vector2 LastPosition = startingPosition;
    public Vector2 LastPositionDelta = Vector2.Zero;
    public bool HasChangedPosition => CurrentPosition != LastPosition;
    public Vector2 PositionDelta => CurrentPosition - LastPosition;
    
    public float CurrentRotation = startingRotation; // In radians
    public float LastRotation = startingRotation; // In radians
    public float LastRotationDelta = 0f;
    public bool HasChangedRotation => CurrentRotation != LastRotation;
    public float RotationDelta => CurrentRotation - LastRotation;
    
    public Vector2 Origin = origin;
}
