namespace MonoDreams.Component.Level;

/// <summary>
/// Added to ECS entities that represent a specific LDtk tile layer
/// to facilitate finding and managing them during level load/unload.
/// </summary>
public readonly struct TilemapLayerComponent
{
    /// <summary>
    /// The instance IID of the LDtk layer this entity represents.
    /// </summary>
    public readonly string LayerInstanceIid;

    public TilemapLayerComponent(string layerInstanceIid)
    {
        LayerInstanceIid = layerInstanceIid;
    }
}