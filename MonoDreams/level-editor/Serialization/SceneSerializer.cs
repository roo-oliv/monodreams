#nullable enable
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
    /// </summary>
    public List<Entity> Deserialize(World world, SceneData scene)
    {
        var created = new List<Entity>(scene.Entities.Count);

        // First pass: create every entity and set its components.
        foreach (var entityData in scene.Entities)
        {
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
}
