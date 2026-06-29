#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Creates a new save-root entity (and its sub-graph) as a <b>reversible</b> command — the mirror of
/// <see cref="DeleteEntityCommand"/>. The editor's placement path supplies a <c>builder</c> that
/// constructs the root entity (e.g. via an <c>IEntityFactory</c> or a direct component build); the
/// command tags that root with <see cref="SceneObjectComponent"/> so it round-trips through the
/// scene writer, then captures a component snapshot of the created sub-graph.
///
/// <para><b>Apply / Revert.</b> The first <see cref="Apply"/> runs the builder, tags + snapshots the
/// result. <see cref="Revert"/> disposes the created sub-graph (undo of a create = delete).
/// A subsequent <see cref="Apply"/> (redo) reconstructs from the snapshot rather than re-running the
/// builder, so redo is deterministic and side-effect-free (the builder may have non-reproducible
/// effects). Children are NOT tagged — the writer auto-closes the <c>ChildOf</c> descendants.</para>
/// </summary>
public sealed class CreateEntityCommand : IEditorCommand
{
    private readonly World _world;
    private readonly SceneSerializer _serializer;
    private readonly Func<World, Entity> _builder;
    private List<Entity> _subgraph = new();
    private SceneData? _snapshot;

    public CreateEntityCommand(World world, SceneSerializer serializer, Func<World, Entity> builder)
    {
        _world = world;
        _serializer = serializer;
        _builder = builder;
    }

    public void Apply(World world)
    {
        if (_snapshot == null)
        {
            // First do: run the builder, tag the root as a save-root, snapshot the sub-graph.
            var root = _builder(world);
            root.Set(new SceneObjectComponent());
            _subgraph = EntitySubgraph.Collect(world, root);
            _snapshot = _serializer.Serialize(_subgraph);
        }
        else
        {
            // Redo: reconstruct from the snapshot (deterministic, no builder side effects).
            var restored = _serializer.Deserialize(world, _snapshot);
            if (restored.Count > 0)
                restored[0].Set(new SceneObjectComponent());
            _subgraph = restored;
            _snapshot = _serializer.Serialize(restored);
        }
    }

    public void Revert(World world)
    {
        foreach (var entity in _subgraph)
            if (entity.IsAlive)
                entity.Dispose();
        _subgraph = new List<Entity>();
    }
}
