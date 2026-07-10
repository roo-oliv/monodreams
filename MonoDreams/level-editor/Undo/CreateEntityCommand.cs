#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Extension;
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
    private readonly Entity _parentTo;
    private List<Entity> _subgraph = new();
    private SceneData? _snapshot;

    /// <param name="parentTo">PF-F auto-parent: when alive (a prefab-context placement), the created root
    /// is parented under it (<c>ChildOf</c>) and loses its <see cref="SceneObjectComponent"/> tag — so a
    /// placement inside a prefab tab becomes a CHILD of the single prefab root, never a second root (a
    /// multi-root prefab is un-savable). Default (a scene placement) keeps the created entity a save-root.
    /// The parent is re-applied on redo too (it is NOT captured by the sub-graph snapshot — the parent is
    /// out of the snapshot's scope — so it must be re-established each Apply).</param>
    public CreateEntityCommand(World world, SceneSerializer serializer, Func<World, Entity> builder,
        Entity parentTo = default)
    {
        _world = world;
        _serializer = serializer;
        _builder = builder;
        _parentTo = parentTo;
    }

    public void Apply(World world)
    {
        if (_snapshot == null)
        {
            // First do: run the builder, tag the root as a save-root, auto-parent (prefab context),
            // snapshot the sub-graph.
            var root = _builder(world);
            root.Set(new SceneObjectComponent());
            AutoParent(root);
            _subgraph = EntitySubgraph.Collect(world, root);
            _snapshot = _serializer.Serialize(_subgraph);
        }
        else
        {
            // Redo: reconstruct from the snapshot (deterministic, no builder side effects), then re-tag +
            // re-parent (the cross-subgraph parent is not in the snapshot, so re-establish it each Apply).
            var restored = _serializer.Deserialize(world, _snapshot);
            if (restored.Count > 0)
            {
                restored[0].Set(new SceneObjectComponent());
                AutoParent(restored[0]);
            }
            _subgraph = restored;
            _snapshot = _serializer.Serialize(restored);
        }
    }

    /// <summary>Auto-parents <paramref name="root"/> under <see cref="_parentTo"/> when it is a live,
    /// distinct entity (a prefab-context placement): the created entity becomes an ordinary
    /// <c>ChildOf</c> descendant of the prefab root (dropping its save-root tag) so the prefab stays a
    /// single connected tree. A no-op otherwise (a scene placement keeps the save-root).</summary>
    private void AutoParent(Entity root)
    {
        if (!_parentTo.IsAlive || _parentTo.Equals(root)) return;
        root.SetParent(_parentTo);
        if (root.Has<SceneObjectComponent>()) root.Remove<SceneObjectComponent>();
    }

    public void Revert(World world)
    {
        foreach (var entity in _subgraph)
            if (entity.IsAlive)
                entity.Dispose();
        _subgraph = new List<Entity>();
    }
}
