namespace MonoDreams.Component.Level;

/// <summary>
/// Added to ECS entities that represent one tile layer of a loaded level, to
/// facilitate finding and managing them during level load/unload. The tag is
/// format-agnostic — the LDtk import parser (<c>level-ldtk</c>) is its only
/// producer today, but nothing about it is LDtk-specific.
/// </summary>
public readonly struct TilemapLayerComponent
{
    /// <summary>
    /// The layer's stable instance id in the source level data (an LDtk layer's
    /// instance IID when the level came in through the LDtk import).
    /// </summary>
    public readonly string LayerInstanceIid;

    public TilemapLayerComponent(string layerInstanceIid)
    {
        LayerInstanceIid = layerInstanceIid;
    }
}