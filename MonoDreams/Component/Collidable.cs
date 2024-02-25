namespace MonoDreams.Component;

public struct Collidable
{
    public bool Passive;

    public Collidable(bool passive = true)
    {
        Passive = passive;
    }
}