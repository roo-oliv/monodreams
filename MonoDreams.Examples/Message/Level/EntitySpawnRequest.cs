using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Message.Level;

/// <summary>
/// Message published when an LDtk entity instance should be spawned into the ECS world.
/// Contains parsed data from the LDtkEntityInstance.
/// </summary>
public readonly struct EntitySpawnRequest
{
    /// <summary>
    /// The identifier of the entity definition in LDtk (e.g., "PlayerStart", "NPC").
    /// </summary>
    public readonly string Identifier;

    /// <summary>
    /// The unique instance identifier (IID) of the entity in the LDtk level.
    /// </summary>
    public readonly string InstanceIid;

    /// <summary>
    /// The world position (top-left) of the entity in pixels.
    /// </summary>
    public readonly Vector2 Position;

    /// <summary>
    /// The size of the entity in pixels.
    /// </summary>
    public readonly Vector2 Size;

    /// <summary>
    /// The pivot point of the entity (0,0 = top-left, 0.5,0.5 = center, 1,1 = bottom-right).
    /// </summary>
    public readonly Vector2 Pivot;

    /// <summary>
    /// A dictionary containing the parsed custom fields for this entity instance.
    /// Keys are field identifiers (names), values are parsed objects (int, float, bool, string, Vector2 for Point, etc.).
    /// </summary>
    public readonly Dictionary<string, object> CustomFields;

    public EntitySpawnRequest(
        string identifier,
        string instanceIid,
        Vector2 position,
        Vector2 size,
        Vector2 pivot,
        Dictionary<string, object> customFields)
    {
        Identifier = identifier;
        InstanceIid = instanceIid;
        Position = position;
        Size = size;
        Pivot = pivot;
        CustomFields = customFields ?? new Dictionary<string, object>();
    }
}