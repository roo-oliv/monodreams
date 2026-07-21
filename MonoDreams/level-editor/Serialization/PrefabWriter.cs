#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Builds a validated, normalized <c>.mdprefab</c> <see cref="SceneData"/> from the live world — the
/// write half of the prefab format. It reuses the scene writer's membership closure + diff-based
/// instance compaction wholesale (a prefab is authored exactly like a scene), then enforces the
/// prefab rules on top:
/// <list type="bullet">
///   <item><b>Exactly ONE root</b> — every other entity must parent-chain to it
///   (<see cref="PrefabData.FindSingleRootIndex"/>); a multi-root world is refused loud.</item>
///   <item><b>Root Transform position normalized to origin</b> — the root's stored position is set to
///   <c>[0,0]</c> (children keep their LOCAL offsets, since <c>TransformComponent.Position</c> is
///   parent-relative), so the prefab is placement-agnostic and an instance's own Transform is the only
///   thing that positions it.</item>
///   <item><b>No camera</b> — a prefab is a class, not a scene; a camera entity inside one is refused
///   (pre-mortem #8: a camera in a prefab is multi-camera terrain — see camera "Exactly one camera
///   entity per scene").</item>
///   <item><b>No cycles</b> — a prefab that (directly or transitively) contains an instance of itself
///   is refused at save (pre-mortem #7); expansion additionally caps depth at load.</item>
/// </list>
///
/// <para>Infrastructure, not a component (ECS purity): it holds the write behaviour and runs at
/// prefab-save time only. The file IO (write the returned <see cref="SceneData"/> through
/// <see cref="CanonicalJson"/> into the source tree + append the MGCB copy line) is the editor
/// overlay's Save-Prefab path (PF-D); this class produces + validates the bytes.</para>
/// </summary>
public sealed class PrefabWriter
{
    /// <summary>The native prefab file extension. A prefab id <c>"npc-boldo"</c> writes to
    /// <c>&lt;PrefabsPath&gt;/npc-boldo.mdprefab</c>.</summary>
    public const string PrefabFileExtension = ".mdprefab";

    private readonly SceneWriter _sceneWriter;

    /// <param name="sceneWriter">The scene writer whose membership closure + diff-based instance
    /// compaction a prefab reuses. If the prefab world itself contains nested prefab instances, the
    /// scene writer must have been constructed with a prefab source so it can compact them.</param>
    public PrefabWriter(SceneWriter sceneWriter) =>
        _sceneWriter = sceneWriter ?? throw new ArgumentNullException(nameof(sceneWriter));

    /// <summary>
    /// Builds the validated, normalized prefab <see cref="SceneData"/> for the contents of
    /// <paramref name="world"/> under <paramref name="prefabId"/>. Uses the scene writer's closure +
    /// compaction (no camera), then enforces one-root + origin-normalization + cycle-refusal.
    /// </summary>
    /// <param name="world">The prefab-tab world (the single tagged root + its closure).</param>
    /// <param name="prefabId">The prefab id (for cycle refusal + loud error messages).</param>
    /// <param name="cycleSource">Optional resolver for OTHER prefabs, used to detect a <b>transitive</b>
    /// self-reference (an instance of X inside a prefab X's file, via a chain of nested prefabs). When
    /// null only a DIRECT self-reference is caught.</param>
    public SceneData BuildPrefab(World world, string prefabId, Func<string, PrefabData?>? cycleSource = null)
    {
        // Reuse the scene writer's membership closure + diff-based instance compaction.
        var scene = _sceneWriter.BuildScene(world);

        // A prefab is a CLASS, not a scene — it REFUSES any camera entity (a camera inside a prefab is
        // multi-camera terrain, CM). The scene writer's BuildScene already refuses ≥2 cameras; a prefab
        // refuses even ONE, so guard here on the serialized entities.
        RefuseAnyCamera(prefabId, scene);

        var rootIndex = PrefabData.FindSingleRootIndex(prefabId, scene); // exactly one root, else loud
        NormalizeRootToOrigin(scene, rootIndex);
        RefuseCycles(prefabId, scene, cycleSource);

        return scene;
    }

    /// <summary>
    /// Refuses (loud) a prefab world carrying ANY <c>core.Camera</c> entity — a camera inside a prefab is
    /// multi-camera terrain (CM). Belt-and-suspenders alongside the scene writer's ≥2 refusal: a prefab
    /// refuses even one.
    /// </summary>
    private static void RefuseAnyCamera(string prefabId, SceneData scene)
    {
        foreach (var entry in scene.Entities)
            if (entry.Components != null && entry.Components.ContainsKey(EngineComponentSerializers.CameraKey))
                throw new InvalidOperationException(
                    $"[level-editor] Prefab '{prefabId}' cannot contain a camera entity ('{EngineComponentSerializers.CameraKey}') — " +
                    "a camera belongs to a scene, not a prefab (a camera-in-prefab is multi-camera terrain). Refused.");
    }

    /// <summary>
    /// Sets the root entry's <c>core.Transform</c> <c>position</c> to <c>[0,0]</c>, preserving its
    /// rotation / scale / origin. Children are untouched — their positions are LOCAL (parent-relative),
    /// so they keep their offsets from the (now origin) root. A root without a Transform is left as-is.
    /// </summary>
    private static void NormalizeRootToOrigin(SceneData scene, int rootIndex)
    {
        var components = scene.Entities[rootIndex].Components;
        if (!components.TryGetValue(EngineComponentSerializers.TransformKey, out var transform))
            return; // no Transform to normalize (defensive)

        var node = JsonNode.Parse(transform.GetRawText())?.AsObject();
        if (node == null) return;

        node["position"] = new JsonArray(JsonValue.Create(0f), JsonValue.Create(0f));
        components[EngineComponentSerializers.TransformKey] = CanonicalJson.SerializeToElement(node);
    }

    /// <summary>
    /// Refuses a prefab that contains an instance of itself, directly or transitively (pre-mortem #7).
    /// A direct self-reference (an <c>entities[]</c> entry whose <c>prefab</c> == <paramref name="prefabId"/>)
    /// is always caught. A transitive cycle (X → Y → … → X) is caught only when <paramref name="cycleSource"/>
    /// resolves the nested prefabs; the visited-set bounds the walk.
    /// </summary>
    private static void RefuseCycles(string prefabId, SceneData scene, Func<string, PrefabData?>? cycleSource)
    {
        var visited = new HashSet<string>();

        void Walk(SceneData current)
        {
            foreach (var entry in current.Entities)
            {
                if (entry.Prefab is not { } childId) continue;
                if (childId == prefabId)
                    throw new InvalidOperationException(
                        $"[level-editor] Prefab '{prefabId}' cannot contain an instance of itself (an entry " +
                        $"references '{childId}') — a prefab cycle would recurse forever on expansion. Refused.");
                if (cycleSource == null) continue;         // only DIRECT self-reference is detectable source-less
                if (!visited.Add(childId)) continue;       // already walked this nested prefab
                var child = cycleSource(childId);
                if (child != null) Walk(child.Scene);
            }
        }

        Walk(scene);
    }
}
