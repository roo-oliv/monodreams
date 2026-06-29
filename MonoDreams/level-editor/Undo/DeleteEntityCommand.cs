#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Deletes an entity and its <c>ChildOf</c> sub-graph as a <b>reversible</b> command. Per the
/// run-state contract, an editor delete is never a bare <c>entity.Dispose()</c>: it
/// <b>snapshots the disposed sub-graph</b> (the root plus its descendants' serialized components,
/// reusing the Wave-2 <see cref="SceneSerializer"/>) at construction time, so <see cref="Revert"/>
/// can reconstruct it from components — including the parent graph.
///
/// <para><b>Why snapshot at construction.</b> The snapshot is taken when the command is built (the
/// entities are still alive), not in <see cref="Apply"/>, because by the time undo runs the live
/// entities are gone. <see cref="Apply"/> then disposes the captured entities; <see cref="Revert"/>
/// deserializes the snapshot into the world. The root's transient <see cref="SceneObjectComponent"/>
/// tag is not in the serializer registry (it is editor state, like <c>VisibleComponent</c>), so the
/// command records whether the root was tagged and re-applies the tag on restore.</para>
///
/// <para>Overlay entities are standalone (never <c>ChildOf</c>-parented to game entities), so this
/// sub-graph delete never reaches into the gizmo/selection overlay. Restored entities are <b>new</b>
/// <see cref="Entity"/> handles — a redo after an undo produces fresh ids, which is why the command
/// re-snapshots the restored root for replay.</para>
/// </summary>
public sealed class DeleteEntityCommand : IEditorCommand
{
    private readonly SceneSerializer _serializer;
    private List<Entity> _subgraph;
    private SceneData _snapshot;
    private readonly bool _rootWasSceneObject;

    /// <summary>Builds the command for <paramref name="root"/>, snapshotting its sub-graph now (the
    /// entities must still be alive). The entities are not disposed until <see cref="Apply"/>.</summary>
    public DeleteEntityCommand(World world, Entity root, SceneSerializer serializer)
    {
        _serializer = serializer;
        _subgraph = EntitySubgraph.Collect(world, root);
        _snapshot = serializer.Serialize(_subgraph);
        _rootWasSceneObject = root.Has<SceneObjectComponent>();
    }

    public void Apply(World world)
    {
        foreach (var entity in _subgraph)
            if (entity.IsAlive)
                entity.Dispose();
        _subgraph = new List<Entity>(); // the handles are dead now
    }

    public void Revert(World world)
    {
        // Reconstruct from the component snapshot (create + deserialize + wire parents).
        var restored = _serializer.Deserialize(world, _snapshot);
        _subgraph = restored;

        // Re-tag the save-root: SceneObjectComponent is transient editor state, not serialized.
        if (_rootWasSceneObject && restored.Count > 0)
            restored[0].Set(new SceneObjectComponent());

        // Re-snapshot from the freshly restored entities so a subsequent redo→undo replays cleanly
        // (the previous snapshot referenced now-dead handles only for the dispose pass).
        _snapshot = _serializer.Serialize(restored);
    }
}
