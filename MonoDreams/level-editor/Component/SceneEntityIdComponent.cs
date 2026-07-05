namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// A <b>persisted, stable, scene-local id</b> for a serialized scene ROOT (a
/// <see cref="SceneObjectComponent"/>-tagged entity with no in-scope parent). Unlike
/// <see cref="EditorIdComponent"/> — which is per-SESSION, reassigned on every run by the selection
/// system as a render tiebreak, and never serialized — this id is written to the scene file (the
/// entity entry's <c>id</c> field), read back on load, and reused on the next save. The scene writer
/// orders <c>entities[]</c> by this id, so a re-save keeps the array order stable: moving one entity's
/// transform is a one-line diff instead of a reshuffle, which is what makes <c>.mdscene</c> files
/// mergeable in git.
///
/// <para><b>Lifecycle.</b> Assigned lazily at the <b>first serialization</b> (<see cref="Serialization.SceneWriter"/>
/// stamps monotonic ids onto any root lacking one — the next free id being max-present + 1),
/// preserved across <c>load → save</c> (the reader restores it from the file, the writer reads it
/// back), and a brand-new root gets the next free id. It is NOT an <see cref="EditorIdComponent"/>
/// overload: the two ids answer different questions (persistent scene identity vs. per-session render
/// tiebreak) and are kept separate.</para>
///
/// <para>Pure data — the monotonic counter that allocates ids lives in the scene writer, not here.
/// Captured as the entity entry's dedicated <c>id</c> field (like the <c>parent</c> link), never as a
/// component body, so it is marked structurally-captured on the serializer registry (never written
/// into <c>components{}</c>, never trips the unregistered-component warning).</para>
/// </summary>
public struct SceneEntityIdComponent
{
    /// <summary>The stable, scene-local id (monotonic within a scene; assigned at first serialization).</summary>
    public int Id;

    public SceneEntityIdComponent(int id) => Id = id;
}
