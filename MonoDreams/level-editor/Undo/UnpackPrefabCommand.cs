#nullable enable
using System;
using DefaultEcs;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// <b>Unpacks</b> a linked prefab instance (PF-D, the escape hatch): dissolves the link by removing the
/// <see cref="PrefabInstanceComponent"/> marker from the instance ROOT, keeping every live entity as an
/// ordinary scene entity. Its children — which were prefab-owned and excluded from the scene file (the
/// membership closure stopped at the instance root) — become closure-serialized again the moment the
/// marker is gone, so the whole subtree persists verbatim on the next Save (and the instance-children
/// guardrails no longer apply — they are ordinary entities).
///
/// <para>Reversible with no dispose: <see cref="Apply"/> removes the marker, <see cref="Revert"/> restores
/// it, so undo re-links the instance (restoring the compact serialization behaviour). The entity handle is
/// stable across the toggle (nothing is disposed), matching the transform/collider edit commands that hold
/// a live handle. Deleting the instance root is a separate, ordinary delete (snapshot-undoable); unpack is
/// specifically "keep the entities, drop the link".</para>
/// </summary>
public sealed class UnpackPrefabCommand : IEditorCommand
{
    private readonly Entity _root;
    private readonly string _prefabId;

    /// <summary>Builds the command for the instance <paramref name="root"/> (must carry
    /// <see cref="PrefabInstanceComponent"/>); captures the prefab id so undo can re-link it.</summary>
    public UnpackPrefabCommand(Entity root)
    {
        _root = root;
        _prefabId = root.IsAlive && root.Has<PrefabInstanceComponent>()
            ? root.Get<PrefabInstanceComponent>().PrefabId
            : throw new ArgumentException("UnpackPrefabCommand requires an entity carrying PrefabInstanceComponent.", nameof(root));
    }

    public void Apply(World world)
    {
        if (_root.IsAlive && _root.Has<PrefabInstanceComponent>())
            _root.Remove<PrefabInstanceComponent>(); // drop the link — the subtree becomes ordinary scene entities
    }

    public void Revert(World world)
    {
        if (_root.IsAlive)
            _root.Set(new PrefabInstanceComponent(_prefabId)); // re-link — the root compacts to a prefab entry again
    }
}
