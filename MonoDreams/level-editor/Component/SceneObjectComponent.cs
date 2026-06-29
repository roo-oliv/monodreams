namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags a <b>save-root</b> entity: an entity the scene writer should persist. The writer
/// serializes every entity that carries this tag <b>plus</b> each one's
/// <c>ChildOfComponent</c> descendant closure, so a factory's sub-graph (e.g. a player and its
/// orbiting orbs) round-trips with its parent graph intact even though only the root is tagged.
///
/// <para>Transient and overlay entities — the cursor, UI / HUD widgets, the editor's own gizmo /
/// selection / toolbar entities — are deliberately left untagged, so they are excluded from the
/// scene. Blender-origin entities are also untagged in this wave (their save is deferred), so the
/// Blender world stays view-only.</para>
///
/// <para>It is a pure tag (no fields) — ECS purity: membership is data on the entity, the closure
/// computation lives in the scene writer. The editor's placement / load paths add this tag to the
/// entities the designer creates (or that a loaded scene reconstructs).</para>
/// </summary>
public struct SceneObjectComponent
{
}
