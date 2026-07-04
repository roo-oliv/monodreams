namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags an entity as a <b>bake product</b> — a runtime-derived output of an authoring source
/// (island-authoring Slice 3: the thin convex quad segment colliders <c>BoundaryBakeSystem</c>
/// generates from a <see cref="BoundaryComponent"/> polyline; any future bake output follows the
/// same rule). A bake product is <b>never scene-serialized</b>: <c>SceneWriter</c> excludes it from
/// the membership closure <b>even inside a tagged root's <c>ChildOf</c> descendant set</b>, because
/// the durable truth is the authoring source (the polyline), and the products regenerate on load /
/// on edit. Re-serializing them would double-count on the next load and bake stale run state into
/// the file.
///
/// <para>The first application of the wave-repass "bake products never scene-serialize" invariant.
/// Pure data (an empty marker), never registered in the component-serializer registry.</para>
/// </summary>
public readonly struct BakedProductComponent;
