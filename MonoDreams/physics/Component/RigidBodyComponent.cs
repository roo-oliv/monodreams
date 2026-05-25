namespace MonoDreams.Component.Physics;

public class RigidBodyComponent
{
    public float Mass;
    // public float Drag;
    // public float AngularDrag;
    public (bool active, float factor) Gravity;
    public bool IsKinematic;
    public bool FreezeRotation;
    public bool FreezePositionX;
    public bool FreezePositionY;
    public bool FreezePosition
    {
        get => FreezePositionX && FreezePositionY;
        set
        {
            FreezePositionX = value;
            FreezePositionY = value;
        }
    }
    public bool FreezeAll
    {
        get => FreezeRotation && FreezePosition;
        set
        {
            FreezeRotation = value;
            FreezePosition = value;
        }
    }

    public RigidBodyComponent(float mass = 1f, bool isKinematic = false, bool freezeRotation = false, bool freezePosition = false, bool gravityActive = true, float gravityScale = 1f)
    {
        Mass = mass;
        IsKinematic = isKinematic;
        FreezeRotation = freezeRotation;
        FreezePosition = freezePosition;
        Gravity = (gravityActive, gravityScale);
    }
}