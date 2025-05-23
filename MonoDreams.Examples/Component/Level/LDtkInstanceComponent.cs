namespace MonoDreams.Examples.Component.Level;

/// <summary>
/// Stores the LDtk instance IID for an ECS entity.
/// </summary>
public struct LDtkInstanceComponent
{
    public string Iid;

    public LDtkInstanceComponent(string iid)
    {
        Iid = iid;
    }
}
