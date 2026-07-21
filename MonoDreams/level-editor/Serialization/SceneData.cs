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
/// The on-disk format mirrors this type 1:1 via System.Text.Json. The full format is specified in
/// <c>MonoDreams/level-editor/docs/scene-format.md</c>.
///
/// <para><b>Wave 2 scope.</b> This type and the registry exist so an entity's registered
/// components can round-trip to/from this in-memory structure (unit-tested with hand-built
/// entities). The file writer/reader and the <c>LoadSceneRequest</c> message land in Wave 3.</para>
/// </summary>
public class SceneData
{
    /// <summary>The current native scene/prefab format version (see <see cref="Version"/>). Lives here —
    /// on the dependency-free format type — so the engine's <see cref="SceneVersionGuard"/> references ONE
    /// constant without pulling in the component-serializer registry. (The CLI collider migrator's own
    /// target version is a separate constant — the collider lift targets v2, the camera lift v3.)</summary>
    public const int CurrentVersion = 3;

    /// <summary>
    /// Scene format version. Bump on any breaking change to the schema.
    ///
    /// <para><b>Version 3 (camera-as-entity, CM).</b> The default is <c>3</c>: everything the writer emits
    /// (scenes AND prefabs) is version 3. The camera is no longer a special <c>camera</c> file block — it
    /// is an ordinary scene entity (<c>EntityInfo("Camera")</c> + <c>Transform</c> + <c>core.Camera</c>
    /// zoom), serialized in <c>entities[]</c> like everything else. The <see cref="Camera"/> block LEFT the
    /// writer; it survives only as a deserialization target so <see cref="SceneVersionGuard"/> can DETECT a
    /// legacy block and the CLI camera migrator can lift it into an entity.</para>
    ///
    /// <para><b>Version 2 (colliders-as-entities, CE-B).</b> A version-2 collider is a shape on its own
    /// collider ENTITY (box <c>size</c> centered on the entity's Transform; convex <c>modelVertices</c>
    /// entity-local). A version-2 file WITHOUT a camera block loads and re-saves as version 3; a version-2
    /// file WITH one is refused on file read by <see cref="SceneVersionGuard"/> (run <c>monodreams
    /// migrate</c>).</para>
    ///
    /// <para><b>Version 1 (legacy).</b> A version-1 file that carries an embedded collider
    /// (a <c>core.BoxCollider</c>/<c>core.ConvexCollider</c> body) is refused on file read (run
    /// <c>monodreams migrate-colliders</c>); a version-1 file WITHOUT colliders or a camera block loads and
    /// re-saves at the current version. In-memory <see cref="SceneData"/> (Game-mode snapshots) is
    /// version-agnostic — only file reads guard.</para>
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// <b>Legacy camera block — deserialization-only (CM).</b> The writer no longer emits it (the camera is
    /// a scene entity now); it survives on this type so <see cref="SceneVersionGuard.CheckFileLoad"/> can
    /// DETECT a pre-CM camera block (a version-2 file carrying one is refused → run <c>monodreams
    /// migrate</c>) and so the CLI camera migrator can read the block it lifts into a camera entity. A
    /// version-3 file never carries it (<see cref="CanonicalJson"/> null-omits it, and nothing sets it).
    /// </summary>
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

/// <summary>Legacy camera state (position / zoom / rotation) — a pre-CM <c>camera</c> file block. The
/// writer no longer produces it; it survives only as the shape <see cref="SceneVersionGuard"/> detects on
/// a legacy file read and the CLI camera migrator lifts into a camera entity.</summary>
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
    /// The <b>persisted, stable, scene-local id</b> of a serialized scene ROOT (see
    /// <c>SceneEntityIdComponent</c>). Assigned at first serialization, preserved across
    /// <c>load → save</c>, and the key <see cref="SceneData.Entities"/> is ordered by on write — so a
    /// re-save keeps the array order stable and a single-entity edit stays a minimal diff. Only roots
    /// carry an id; a <c>ChildOf</c> descendant leaves it <c>null</c> (it is ordered within its
    /// ancestor's closure, not by an id of its own). Structural metadata like <see cref="Parent"/>,
    /// not a component body; omitted from the file when <c>null</c>.
    /// </summary>
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    /// <summary>
    /// The <b>prefab id</b> of a <b>linked prefab instance</b> (see <c>PrefabInstanceComponent</c>): the
    /// entry is an instance of <c>Content/Prefabs/&lt;prefab&gt;.mdprefab</c>. <b>Additive + optional</b>:
    /// <c>null</c> (omitted from the file by <see cref="Serialization.CanonicalJson"/>'s null-omission) =
    /// an ORDINARY entity — every pre-prefab scene is byte-identical. When set, this entry is COMPACT:
    /// <see cref="Components"/> holds ONLY <c>core.Transform</c> (always instance-owned) plus the
    /// <b>overridden</b> components (whole-component replacements whose canonical bytes differ from the
    /// prefab root's), and the instance's children are NOT serialized (they come from the prefab — the
    /// writer excludes them from the membership closure). Like <see cref="Id"/>, this is structural
    /// metadata, not a component body.
    /// </summary>
    [JsonPropertyName("prefab")]
    public string? Prefab { get; set; }

    /// <summary>
    /// componentTypeKey → serialized fields (a JSON object per component). The key is the stable
    /// string the registry assigns a component <c>Type</c>; the value is whatever
    /// that type's writer produced. Only registered components appear here — unregistered
    /// components on the live entity are skipped with a loud warning at write time. The canonical
    /// writer emits these keys in ordinal-sorted order (deterministic, independent of live
    /// component-storage order).
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
