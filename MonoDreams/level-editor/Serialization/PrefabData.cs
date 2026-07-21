#nullable enable
using System;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// A validated in-memory <c>.mdprefab</c>: the reused <see cref="SceneData"/> schema (same
/// <see cref="CanonicalJson"/>, same component-serializer registry, same stable ids) with the
/// <b>prefab rule enforced — exactly ONE root</b>. A prefab is a class: a single root entity plus its
/// <c>ChildOf</c> descendant closure; there is no scene-level <c>camera</c> and no second top-level
/// entity.
///
/// <para>The on-disk shape IS <see cref="SceneData"/> (a <c>.mdprefab</c> reads/writes through
/// <see cref="CanonicalJson"/> exactly like a <c>.mdscene</c>); this thin wrapper adds the prefab id
/// and the resolved <see cref="RootIndex"/> so expansion (the reader / factory / propagation) and
/// compaction (the writer) can find the single root without re-deriving it. Construction fails loud
/// on a malformed prefab (zero or multiple roots, or an entry that does not parent-chain to the
/// root) — the unknown-component stance's sibling.</para>
/// </summary>
public sealed class PrefabData
{
    /// <summary>The prefab id (the file <c>Content/Prefabs/&lt;Id&gt;.mdprefab</c>).</summary>
    public string Id { get; }

    /// <summary>The reused scene schema holding the prefab's root + descendant closure.</summary>
    public SceneData Scene { get; }

    /// <summary>The index (into <see cref="SceneData.Entities"/>) of the single prefab root.</summary>
    public int RootIndex { get; }

    /// <summary>The single root entry (the entity every other entry parent-chains to).</summary>
    public SceneEntityData Root => Scene.Entities[RootIndex];

    private PrefabData(string id, SceneData scene, int rootIndex)
    {
        Id = id;
        Scene = scene;
        RootIndex = rootIndex;
    }

    /// <summary>
    /// Wraps <paramref name="scene"/> as a validated prefab under <paramref name="id"/>. Throws
    /// <see cref="InvalidOperationException"/> (loud) when the prefab rule is violated: not exactly one
    /// root, or an entry that does not parent-chain to that root.
    /// </summary>
    public static PrefabData FromScene(string id, SceneData scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        var rootIndex = FindSingleRootIndex(id, scene);
        return new PrefabData(id, scene, rootIndex);
    }

    /// <summary>
    /// Resolves the single root of a prefab <paramref name="scene"/> and validates the prefab rule:
    /// exactly ONE top-level entry (no in-scope <see cref="SceneEntityData.Parent"/>), and every other
    /// entry parent-chains to it (no orphan / no parent cycle that misses the root). Returns the root
    /// index; throws <see cref="InvalidOperationException"/> otherwise. Used on both write (save refuses
    /// a multi-root prefab) and read (a malformed prefab aborts the expansion).
    /// </summary>
    public static int FindSingleRootIndex(string id, SceneData scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        var entries = scene.Entities;
        var count = entries.Count;

        var rootIndex = -1;
        for (var i = 0; i < count; i++)
        {
            if (IsTopLevel(entries[i], count))
            {
                if (rootIndex >= 0)
                    throw new InvalidOperationException(
                        $"[level-editor] Prefab '{id}' has more than one root entity (entries {rootIndex} and {i} " +
                        "have no parent). A prefab must have EXACTLY one root — every other entity must parent-chain " +
                        "to it. Refused.");
                rootIndex = i;
            }
        }

        if (rootIndex < 0)
            throw new InvalidOperationException(
                $"[level-editor] Prefab '{id}' has no root entity (every entry has a parent — a parent cycle). " +
                "A prefab must have exactly one root. Refused.");

        // Every entry must parent-chain to the single root (bounded walk, defends against a malformed cycle).
        for (var i = 0; i < count; i++)
        {
            if (i == rootIndex) continue;
            var cur = i;
            var reached = false;
            for (var step = 0; step < count + 1; step++)
            {
                var parent = entries[cur].Parent;
                if (parent is not { } p || p < 0 || p >= count) break; // top-level (should be the root) or out-of-range
                cur = p;
                if (cur == rootIndex) { reached = true; break; }
            }
            if (!reached)
                throw new InvalidOperationException(
                    $"[level-editor] Prefab '{id}' entry {i} does not parent-chain to the single root (entry " +
                    $"{rootIndex}). A prefab must be one connected tree. Refused.");
        }

        return rootIndex;
    }

    /// <summary>A top-level entry: a null parent, or a parent index outside the entry range (an
    /// out-of-scope parent becomes a root on load — matching <see cref="SceneSerializer.Deserialize"/>).</summary>
    private static bool IsTopLevel(SceneEntityData entry, int count) =>
        entry.Parent is not { } p || p < 0 || p >= count;
}
