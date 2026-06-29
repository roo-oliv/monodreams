#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// In-memory representation of a native MonoDreams scene — the shape the scene writer
/// (Wave 3) emits as JSON and the scene reader parses back. It is plain data: the
/// <see cref="ComponentSerializerRegistry"/> populates <see cref="SceneEntityData.Components"/>
/// from an entity's registered components, never by re-running factories.
///
/// The on-disk format mirrors this type 1:1 via System.Text.Json (consistent with
/// <c>BlenderLevelData</c>). The full format is specified in
/// <c>MonoDreams/level-editor/docs/scene-format.md</c>.
///
/// <para><b>Wave 2 scope.</b> This type and the registry exist so an entity's registered
/// components can round-trip to/from this in-memory structure (unit-tested with hand-built
/// entities). The file writer/reader and the <c>LoadSceneRequest</c> message land in Wave 3.</para>
/// </summary>
public class SceneData
{
    /// <summary>Scene format version. Bump on any breaking change to the schema.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Camera state (position / zoom / rotation) captured at save time.</summary>
    [JsonPropertyName("camera")]
    public SceneCameraData? Camera { get; set; }

    /// <summary>
    /// Named draw-depth layers with their depth ranges and Y-sort flag. Mirrors the
    /// game's draw-layer map; lets the editor reconstruct layer banding on load.
    /// </summary>
    [JsonPropertyName("layers")]
    public List<SceneLayerData> Layers { get; set; } = new();

    /// <summary>
    /// Reserved for later parametric-source waves (Waves D–F: ground splatmap / road / scatter
    /// sources). Empty in Wave 2; documented as reserved so the schema is forward-stable and a
    /// reader can ignore it without a version bump.
    /// </summary>
    [JsonPropertyName("sources")]
    public List<JsonElement> Sources { get; set; } = new();

    /// <summary>The serialized entities. Each carries its registered components and an optional parent ref.</summary>
    [JsonPropertyName("entities")]
    public List<SceneEntityData> Entities { get; set; } = new();
}

/// <summary>Camera state persisted with the scene.</summary>
public class SceneCameraData
{
    [JsonPropertyName("position")]
    public float[] Position { get; set; } = { 0f, 0f };

    [JsonPropertyName("zoom")]
    public float Zoom { get; set; } = 1f;

    [JsonPropertyName("rotation")]
    public float Rotation { get; set; }
}

/// <summary>A named draw-depth layer with its [min,max] depth range and a Y-sort flag.</summary>
public class SceneLayerData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Inclusive depth range as [min, max] (LayerDepth units).</summary>
    [JsonPropertyName("depth")]
    public float[] Depth { get; set; } = { 0f, 0f };

    /// <summary>Whether entities on this layer are Y-sorted (depth derived per-frame from world Y).</summary>
    [JsonPropertyName("ySorted")]
    public bool YSorted { get; set; }
}

/// <summary>
/// A single serialized entity: its registered components keyed by stable component-type key,
/// plus an optional reference to its parent entity (by index into <see cref="SceneData.Entities"/>).
/// </summary>
public class SceneEntityData
{
    /// <summary>
    /// componentTypeKey → serialized fields (a JSON object per component). The key is the stable
    /// string the registry assigns a component <c>Type</c>; the value is whatever
    /// that type's writer produced. Only registered components appear here — unregistered
    /// components on the live entity are skipped with a loud warning at write time.
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, JsonElement> Components { get; set; } = new();

    /// <summary>
    /// Index (into <see cref="SceneData.Entities"/>) of this entity's structural parent
    /// (<c>ChildOfComponent</c>), or <c>null</c> for a root. Index-based so the parent graph
    /// round-trips without persisting volatile <c>Entity</c> ids.
    /// </summary>
    [JsonPropertyName("parent")]
    public int? Parent { get; set; }
}
