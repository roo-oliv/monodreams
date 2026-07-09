#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Extension;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Serializes a set of live entities to a <see cref="SceneData"/> and reconstructs them back,
/// preserving the structural parent graph (<c>ChildOfComponent</c>) via index references. This is
/// the in-memory round-trip seam: it has no file I/O and no <c>LoadSceneRequest</c> dependency, so
/// it is unit-testable with hand-built entities (Wave 2). Wave 3's writer/reader layers JSON file
/// I/O + the <c>LoadSceneRequest</c> message + <c>Texture2D</c> rehydration on top of it.
///
/// <para>The round-trip reconstructs from components, never by re-running factories: each entity's
/// registered components are written, and on load the same components are re-set. The parent graph
/// is captured as <see cref="SceneEntityData.Parent"/> (an index into <see cref="SceneData.Entities"/>)
/// and re-wired in a second pass after every entity exists.</para>
/// </summary>
public sealed class SceneSerializer(ComponentSerializerRegistry registry)
{
    /// <summary>
    /// Serializes <paramref name="entities"/> into a fresh <see cref="SceneEntityData"/> list,
    /// preserving each entity's parent link (when the parent is also in <paramref name="entities"/>).
    /// Camera / layers / sources are the caller's to fill on the returned <see cref="SceneData"/>.
    /// </summary>
    public SceneData Serialize(IReadOnlyList<Entity> entities)
    {
        var scene = new SceneData();

        // First pass: assign each in-scope entity an index and serialize its components.
        var indexOf = new Dictionary<Entity, int>();
        for (var i = 0; i < entities.Count; i++)
            indexOf[entities[i]] = i;

        foreach (var entity in entities)
            scene.Entities.Add(registry.SerializeEntity(entity));

        // Second pass: record the parent index for any entity whose ChildOf parent is in scope.
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!entity.Has<ChildOfComponent>()) continue;
            var parent = entity.Get<ChildOfComponent>().Parent;
            if (parent.IsAlive && indexOf.TryGetValue(parent, out var parentIndex))
                scene.Entities[i].Parent = parentIndex;
            // A parent outside the serialized set leaves Parent null (the entity becomes a root on load).
        }

        return scene;
    }

    /// <summary>
    /// Reconstructs the entities described by <paramref name="scene"/> into <paramref name="world"/>
    /// and returns them in the same order/index as <see cref="SceneData.Entities"/>. Two passes:
    /// create + deserialize each entity's components, then wire the parent graph from the recorded
    /// indices (so <c>SetParent</c> can sync <c>TransformComponent.Parent</c> with both transforms present).
    ///
    /// <para><b>Prefab-instance entries (<see cref="SceneEntityData.Prefab"/> set).</b> A compact instance
    /// entry is NOT created as a plain entity — it is expanded into a full linked-instance subtree by
    /// <paramref name="expandPrefab"/> (the ONE expansion implementation, injected by the reader / factory /
    /// propagation), whose returned ROOT stands in for the entry at its index (so the index-based parent
    /// wiring and re-tag still hold; the subtree's prefab-owned children are extra and not indexed). When
    /// an entry carries a <c>prefab</c> id but NO expander is composed, the load <b>fails loud</b> — a
    /// prefab instance the runtime cannot expand is the missing-entity class of bug (the unknown-component
    /// stance's sibling), never a silently half-created entity.</para>
    /// </summary>
    /// <param name="expandPrefab">Optional prefab expander: <c>(world, instanceEntry) → instance root</c>.
    /// Invoked for each entry whose <see cref="SceneEntityData.Prefab"/> is set. Null on the pure
    /// round-trip path (no prefab entries) and on a legacy reader with no prefab support.</param>
    public List<Entity> Deserialize(World world, SceneData scene, Func<World, SceneEntityData, Entity>? expandPrefab = null)
    {
        var created = new List<Entity>(scene.Entities.Count);

        // First pass: create every entity and set its components (a prefab-instance entry is expanded
        // into a full subtree instead, its root standing in at this index).
        foreach (var entityData in scene.Entities)
        {
            if (entityData.Prefab is { } prefabId)
            {
                if (expandPrefab == null)
                    throw new InvalidOperationException(
                        $"Scene entry references prefab '{prefabId}' but no prefab expander is composed. " +
                        "Compose a PrefabExpander (a prefab source + registry) before loading a scene with " +
                        "prefab instances (the missing-prefab fail-loud stance).");
                created.Add(expandPrefab(world, entityData));
                continue;
            }

            var entity = world.CreateEntity();
            registry.DeserializeEntity(entity, entityData);
            created.Add(entity);
        }

        // Second pass: wire parents now that every entity (and its TransformComponent) exists.
        for (var i = 0; i < scene.Entities.Count; i++)
        {
            var parentIndex = scene.Entities[i].Parent;
            if (parentIndex is { } pi && pi >= 0 && pi < created.Count)
                created[i].SetParent(created[pi]);
        }

        return created;
    }

    /// <summary>The component-serializer registry this serializer reads/writes through — exposed so the
    /// prefab expander can apply whole-component overrides (<c>registry.GetByKey(key).Read</c>) onto an
    /// instance root and so callers share the ONE registry instance.</summary>
    public ComponentSerializerRegistry Registry => registry;
}
