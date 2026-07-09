#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Stamps a <b>linked prefab instance</b> at a world position as a reversible command (PF-D) — the
/// prefab sibling of <see cref="CreateEntityCommand"/>. Unlike a plain create, a linked instance is
/// <b>deterministically reconstructable</b> from its <c>(prefabId, position)</c> through the ONE
/// <see cref="PrefabExpander"/>, so this command needs no serialized sub-graph snapshot: redo simply
/// re-instantiates. Placing runs the expander (root + prefab-owned children, textures rehydrated, the
/// <see cref="PrefabInstanceComponent"/> marker stamped), positions the root, and tags it
/// <see cref="SceneObjectComponent"/> so it round-trips as a compact <c>prefab</c> entry on Save.
///
/// <para><b>Apply / Revert.</b> <see cref="Apply"/> instantiates + positions + tags, and records the
/// created sub-graph so <see cref="Revert"/> disposes it (undo of a create = delete; nothing dangles —
/// the whole instance subtree goes). A redo (a subsequent <see cref="Apply"/>) re-instantiates from the
/// prefab, so a re-edited prefab even re-propagates into a redone instance for free (expansion IS
/// propagation).</para>
/// </summary>
public sealed class CreateInstanceCommand : IEditorCommand
{
    private readonly PrefabExpander _expander;
    private readonly string _prefabId;
    private readonly Vector2 _position;
    private List<Entity> _subtree = new();

    public CreateInstanceCommand(PrefabExpander expander, string prefabId, Vector2 position)
    {
        _expander = expander ?? throw new ArgumentNullException(nameof(expander));
        _prefabId = prefabId ?? throw new ArgumentNullException(nameof(prefabId));
        _position = position;
    }

    /// <summary>The instance root created by the most recent <see cref="Apply"/> (default before the
    /// first apply / after a revert) — the placement path auto-selects it.</summary>
    public Entity Root { get; private set; }

    public void Apply(World world)
    {
        // The ONE expansion path: reconstructs root + prefab-owned children, rehydrates textures + the
        // transient DrawComponent, and stamps the PrefabInstanceComponent marker. Placement mirrors
        // PrefabFactory: the prefab root's Transform is normalized to origin, so this positions it.
        var root = _expander.Instantiate(world, _prefabId);
        if (root.Has<TransformComponent>()) root.Get<TransformComponent>().Position = _position;
        else root.Set(new TransformComponent(_position));

        // A first-class scene object (savable, compacted to {prefab + Transform + overrides} on Save).
        root.Set(new SceneObjectComponent());
        Root = root;

        // Record the created sub-graph (root + prefab-owned children) so Revert disposes the whole instance.
        _subtree = EntitySubgraph.Collect(world, root);
    }

    public void Revert(World world)
    {
        foreach (var entity in _subtree)
            if (entity.IsAlive)
                entity.Dispose();
        _subtree = new List<Entity>();
        Root = default;
    }
}
