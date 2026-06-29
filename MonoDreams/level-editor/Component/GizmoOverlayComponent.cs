namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags an entity created and owned by the gizmo/selection-highlight machinery — the visible
/// overlay meshes the editor draws over the selected entity (the selection outline, the move /
/// rotate / scale handles). It lets <c>GizmoSystem</c> find and tear down its own overlay entities
/// without disturbing game entities.
///
/// <para><b>Standalone, never parented.</b> Per the editor tenet, overlay entities are <b>never</b>
/// <c>ChildOfComponent</c>-parented to the game entity they decorate, because
/// <c>HierarchySystem.DisposeOrphans</c> runs in Edit and would cascade-dispose them when their host
/// is deleted. They carry their own <c>TransformComponent</c> (identity) and set
/// <c>VisibleComponent</c> themselves (<c>CullingSystem</c> only visits <c>SpriteInfoComponent</c>
/// entities, so a bare mesh overlay must self-tag Visible to render to Main). They are transient
/// editor state — untagged by <c>SceneObjectComponent</c>, so the writer excludes them — and absent
/// from the serializer registry.</para>
/// </summary>
public struct GizmoOverlayComponent
{
}
