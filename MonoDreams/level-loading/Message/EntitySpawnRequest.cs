using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.Message;

/// <summary>
/// Message published when an entity should be spawned into the ECS world. Carries the entity's
/// identifier plus placement data; <c>EntitySpawnSystem</c> routes it to the registered
/// <c>IEntityFactory</c> for that identifier (exact match, else a prefix channel like <c>"prefab:"</c>).
///
/// <para>The message is <b>format-agnostic</b>: it carries no LDtk (or any other importer's) types.
/// Importer-specific extras ride in <see cref="CustomFields"/> under a namespaced key — the LDtk
/// parsers publish layer opacity / grid size under the <c>ldtk:</c> keys in
/// <c>LDtkSpawnFields</c> (level-ldtk).</para>
/// </summary>
public readonly struct EntitySpawnRequest
{
    /// <summary>
    /// The identifier of the entity definition (e.g., "PlayerStart", "NPC", "prefab:door").
    /// </summary>
    public readonly string Identifier;

    /// <summary>
    /// A unique instance identifier for this spawn, or empty for code-driven spawns.
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

    public readonly Vector2 TilesetPosition;

    /// <summary>
    /// A dictionary containing the parsed custom fields for this entity instance.
    /// Keys are field identifiers (names), values are parsed objects (int, float, bool, string, Vector2 for Point, etc.).
    /// Importer-derived extras use a namespaced key (see <c>LDtkSpawnFields</c>).
    /// </summary>
    public readonly Dictionary<string, object> CustomFields;

    public EntitySpawnRequest(string identifier,
        string instanceIid,
        Vector2 position,
        Vector2 size,
        Vector2 pivot,
        Vector2 tilesetPosition,
        Dictionary<string, object> customFields)
    {
        Identifier = identifier;
        InstanceIid = instanceIid;
        Position = position;
        Size = size;
        Pivot = pivot;
        TilesetPosition = tilesetPosition;
        CustomFields = customFields ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// A lightweight spawn request — just an <paramref name="identifier"/> and a world
    /// <paramref name="position"/> — for code-driven spawns (e.g. the <c>"prefab:&lt;id&gt;"</c> channel:
    /// <c>new EntitySpawnRequest("prefab:npc-boldo", pos)</c>). Size / pivot / tileset default to zero and
    /// custom fields are empty.
    /// </summary>
    public EntitySpawnRequest(string identifier, Vector2 position)
    {
        Identifier = identifier;
        InstanceIid = string.Empty;
        Position = position;
        Size = Vector2.Zero;
        Pivot = Vector2.Zero;
        TilesetPosition = Vector2.Zero;
        CustomFields = new Dictionary<string, object>();
    }
}
