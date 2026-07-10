#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Undo;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The ONE shared "capture a selection's subtree into a validated prefab <see cref="SceneData"/>"
/// helper (PF-F) — the build half of Create-Prefab-from-Selection, extracted so the overlay and the
/// tests exercise the EXACT same logic (no drift between the real path and its test double). It:
/// <list type="bullet">
///   <item>collects the selection's <c>ChildOf</c> subtree (<see cref="EntitySubgraph.Collect"/>),
///   serializes it, and re-deserializes it into a throwaway world;</item>
///   <item><b>finds the parentless root ROBUSTLY</b> — the entity with no in-scope <c>ChildOf</c>
///   parent — rather than assuming index 0 (the historical bug: a wrong root tag emptied the
///   capture);</item>
///   <item><b>refuses an EMPTY capture</b> — a single bare-<c>Transform</c> root with no children and
///   no other components is "nothing captured" (the user's <c>elephant-kid</c> empty-shell symptom);
///   Create-Empty is the intended path for a bare root;</item>
///   <item><b>names the root</b> — preserves the source root's <see cref="EntityInfoComponent"/>,
///   stamping <c>EntityInfoComponent(prefabId)</c> only when it has none;</item>
///   <item>builds + validates the prefab (<see cref="PrefabWriter.BuildPrefab"/>: one-root +
///   origin-normalize + no camera + cycle-refuse).</item>
/// </list>
/// Returns the built scene + its entity count on success, or a human refusal reason on failure. Pure
/// logic — no file IO, no GraphicsDevice; the caller writes the bytes + surfaces the message.
/// </summary>
public static class PrefabCapture
{
    /// <summary>The outcome of a capture: on success <see cref="Scene"/> + <see cref="EntityCount"/>
    /// are set and <see cref="Refusal"/> is null; on failure <see cref="Refusal"/> names why (loud +
    /// status), <see cref="Scene"/> is null.</summary>
    public readonly record struct Result(SceneData? Scene, int EntityCount, string? Refusal)
    {
        public bool Ok => Scene != null && Refusal == null;

        public static Result Refused(string reason) => new(null, 0, reason);
        public static Result Ok_(SceneData scene) => new(scene, scene.Entities.Count, null);
    }

    /// <summary>The refusal message for an empty capture (also the status text — ASCII only).</summary>
    public const string EmptyRefusal = "selection appears empty - nothing captured";

    /// <summary>
    /// Builds the prefab <see cref="SceneData"/> for the subtree rooted at <paramref name="selectionRoot"/>
    /// under <paramref name="prefabId"/>. See the class doc for the steps. <paramref name="prefabSource"/>
    /// is the resolver used for the writer's compaction + transitive-cycle refusal (may be null).
    /// </summary>
    public static Result Build(World world, Entity selectionRoot, string prefabId,
        SceneSerializer serializer, Func<string, PrefabData?>? prefabSource)
    {
        if (!selectionRoot.IsAlive) return Result.Refused(EmptyRefusal);

        var subgraph = EntitySubgraph.Collect(world, selectionRoot);
        var captured = serializer.Serialize(subgraph);

        // Refuse an empty capture up front: a lone entity whose only component is core.Transform is a
        // bare shell (nothing to a prefab). Checked on the CAPTURED bytes, before any naming stamps a
        // component onto it — so a genuinely-empty selection is always refused.
        if (IsBareTransformOnly(captured))
            return Result.Refused(EmptyRefusal);

        try
        {
            using var tmp = new World();
            var created = serializer.Deserialize(tmp, captured);
            if (created.Count == 0) return Result.Refused(EmptyRefusal);

            // Find the parentless root(s) ROBUSTLY (never `created[0]`): an entity with no in-scope live
            // ChildOf parent. Tag every one so the writer's membership closure covers the whole set (a
            // stray disconnected piece then trips BuildPrefab's one-root refusal instead of being silently
            // dropped). For the normal single-tree selection there is exactly one.
            Entity mainRoot = default;
            var rootCount = 0;
            foreach (var e in created)
            {
                if (!IsParentless(e)) continue;
                e.Set(new SceneObjectComponent());
                if (!mainRoot.IsAlive) mainRoot = e;
                rootCount++;
            }
            if (!mainRoot.IsAlive) return Result.Refused(EmptyRefusal);

            // Name the (single) root: preserve its EntityInfo, else stamp EntityInfo(prefabId).
            if (rootCount == 1 && !mainRoot.Has<EntityInfoComponent>())
                mainRoot.Set(new EntityInfoComponent(prefabId));

            var scene = new PrefabWriter(new SceneWriter(serializer, prefabSource))
                .BuildPrefab(tmp, prefabId, prefabSource);
            return Result.Ok_(scene);
        }
        catch (Exception ex)
        {
            return Result.Refused(ex.Message);
        }
    }

    /// <summary>Whether the captured scene is a single entity whose ONLY component is
    /// <c>core.Transform</c> (a bare root, nothing captured).</summary>
    private static bool IsBareTransformOnly(SceneData scene) =>
        scene.Entities.Count == 1
        && scene.Entities[0].Prefab == null
        && scene.Entities[0].Components.Count == 1
        && scene.Entities[0].Components.ContainsKey(EngineComponentSerializers.TransformKey);

    /// <summary>An entity with no in-scope live <c>ChildOf</c> parent — a root of the deserialized
    /// subtree (the serializer leaves an out-of-scope parent unwired, so the true root has no
    /// <c>ChildOfComponent</c> at all).</summary>
    private static bool IsParentless(Entity e) =>
        !e.Has<ChildOfComponent>() || !e.Get<ChildOfComponent>().Parent.IsAlive;
}
