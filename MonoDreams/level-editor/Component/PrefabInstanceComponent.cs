namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Marks an entity as the ROOT of a <b>linked prefab instance</b>: it was expanded from a
/// <c>.mdprefab</c> file identified by <see cref="PrefabId"/>, and it stays LINKED — edits to the
/// prefab propagate to this instance (on the next scene load, and live on prefab-save). The
/// instance's ordinary children are plain <c>ChildOf</c> descendants of this root, reconstructed
/// from the prefab on every expansion; they carry no prefab marker of their own (the membership
/// closure treats them as prefab-owned and excludes them from the scene file — see
/// <c>SceneWriter.CollectMembership</c>).
///
/// <para><b>Structurally captured, like <see cref="SceneEntityIdComponent"/>.</b> This marker is
/// written to the scene entry's dedicated <c>prefab</c> field (<c>SceneEntityData.Prefab</c>), never
/// as a body inside <c>components{}</c>. It is marked structurally-captured on the serializer registry
/// (<c>EngineComponentSerializers.RegisterEngineComponents</c>), so the component discoverer silently
/// skips it (it never trips the unregistered-component warning) and the editable Inspector hides it
/// (never a designer-editable/addable/removable row — see <c>ComponentSerializerRegistry.IsStructural</c>).</para>
///
/// <para>Pure data. The expansion machinery (<c>PrefabExpander</c>) stamps it on the instance root
/// after reconstructing the subtree and applying the instance's whole-component overrides; the
/// compacting writer (<c>SceneWriter</c>) reads it to emit the compact <c>prefab</c> + Transform +
/// overrides entry and to stop the closure descent at the root.</para>
/// </summary>
public struct PrefabInstanceComponent
{
    /// <summary>The id of the source <c>.mdprefab</c> this entity is a linked instance of (the file
    /// <c>Content/Prefabs/&lt;PrefabId&gt;.mdprefab</c>).</summary>
    public string PrefabId;

    public PrefabInstanceComponent(string prefabId) => PrefabId = prefabId;
}
