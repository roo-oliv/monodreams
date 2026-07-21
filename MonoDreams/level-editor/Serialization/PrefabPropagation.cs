#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Live prefab propagation: rebuilds every open instance of a prefab from its (just-saved) new
/// definition, preserving each instance's overrides. Expansion IS propagation — a scene load already
/// refreshes instances from the prefab; this is the same mechanism applied to the entities already in
/// the world when a prefab is re-saved, so an edit to <c>npc-boldo.mdprefab</c> shows on every placed
/// <c>npc-boldo</c> without a reload.
///
/// <para>For each instance root of the target prefab in <paramref name="world"/>: capture its current
/// <b>override set</b> (diff its live components against the OLD prefab it currently reflects — the
/// same <see cref="PrefabDiff"/> the writer uses), remember its scene id, despawn the instance
/// (root + prefab-owned children), then rebuild it through the SAME <see cref="PrefabExpander"/> (whose
/// source now resolves the NEW prefab) with the captured overrides re-applied, re-tagging the root a
/// scene object and restoring its scene id. The rebuilt instance = new prefab content + the
/// designer's overrides.</para>
///
/// <para><b>The Restart rule (pre-mortem #2).</b> A live rebuild disposes and recreates entities, so
/// any undo/redo command that referenced the old entities would dangle. If ANY instance was rebuilt,
/// the editor history is <b>cleared</b> and the scene marked <b>dirty</b> (the status-bar note) — the
/// same discarded-unsaved-edits contract as the transport's Restart. When NO instance of the prefab is
/// open, the history is left completely untouched. Smarter command-merging is named terrain.</para>
/// </summary>
public static class PrefabPropagation
{
    /// <summary>
    /// Re-expands every instance of <paramref name="prefabId"/> in <paramref name="world"/> from its new
    /// definition (resolved through <paramref name="expander"/>'s source), preserving overrides diffed
    /// against <paramref name="oldPrefab"/>. Returns the number of instances rebuilt. When that is &gt; 0
    /// and <paramref name="history"/> is supplied, the history is cleared and the scene marked dirty (the
    /// Restart rule); when it is 0 the history is untouched.
    /// </summary>
    /// <param name="world">The world holding the open instances.</param>
    /// <param name="prefabId">The prefab whose instances to refresh.</param>
    /// <param name="oldPrefab">The prefab definition the open instances currently reflect (captured
    /// BEFORE the save overwrote the file) — used only to compute each instance's current override set.
    /// The NEW definition is resolved by <paramref name="expander"/> when it rebuilds.</param>
    /// <param name="expander">The ONE expansion implementation; its source must now resolve the NEW
    /// prefab for <paramref name="prefabId"/> (i.e. the file was already saved).</param>
    /// <param name="registry">The registry used to serialize an instance root's live components for the
    /// override diff.</param>
    /// <param name="history">The editor history to clear + dirty when instances were rebuilt (optional).</param>
    public static int ReExpand(
        World world,
        string prefabId,
        PrefabData oldPrefab,
        PrefabExpander expander,
        ComponentSerializerRegistry registry,
        EditorHistory? history = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (oldPrefab == null) throw new ArgumentNullException(nameof(oldPrefab));
        if (expander == null) throw new ArgumentNullException(nameof(expander));
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        // Snapshot the instance roots to refresh BEFORE mutating (dispose/recreate changes the world set;
        // the rebuilt roots carry the marker too and must not be re-processed).
        var instances = new List<Entity>();
        using (var set = world.GetEntities().With<PrefabInstanceComponent>().AsSet())
            foreach (var e in set.GetEntities())
                if (e.Get<PrefabInstanceComponent>().PrefabId == prefabId)
                    instances.Add(e);

        if (instances.Count == 0) return 0; // no open instance → nothing rebuilt, history untouched

        // Index children by parent once (over the whole world) so each instance's prefab-owned subtree
        // can be disposed; subtrees are disjoint across instances, so the map stays valid per-instance.
        var childrenByParent = BuildChildrenIndex(world);

        foreach (var root in instances)
        {
            if (!root.IsAlive) continue;

            // Capture the current overrides (diff live components vs the OLD prefab root) + the scene id.
            var full = registry.SerializeEntity(root).Components;
            var overrides = PrefabDiff.ComputeOverrides(full, oldPrefab.Root.Components);
            int? sceneId = root.Has<SceneEntityIdComponent>() ? root.Get<SceneEntityIdComponent>().Id : null;

            // Despawn the instance (root + prefab-owned children), then rebuild from the NEW prefab.
            DisposeSubtree(root, childrenByParent);

            var rebuilt = expander.Instantiate(world, prefabId, overrides);
            rebuilt.Set(new SceneObjectComponent()); // a scene object (like the reader's re-tag / the factory)
            if (sceneId is { } id) rebuilt.Set(new SceneEntityIdComponent(id)); // preserve scene identity
        }

        // The Restart rule: a live rebuild dangles undo/redo commands → clear the history and mark dirty.
        if (history != null)
        {
            history.Clear();      // drops undo + redo (and re-marks clean)
            history.MarkDirty();  // ...then flag the propagation edit as unsaved (the status-bar note)
        }

        Logger.Info($"[level-editor] Propagated prefab '{prefabId}' to {instances.Count} open instance(s); " +
                    "undo history cleared (the Restart rule).");
        return instances.Count;
    }

    /// <summary>Children-by-parent over every <c>ChildOf</c> entity in the world (a live parent only).</summary>
    private static Dictionary<Entity, List<Entity>> BuildChildrenIndex(World world)
    {
        var map = new Dictionary<Entity, List<Entity>>();
        using var set = world.GetEntities().With<ChildOfComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            var parent = e.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) continue;
            if (!map.TryGetValue(parent, out var list)) map[parent] = list = new List<Entity>();
            list.Add(e);
        }
        return map;
    }

    /// <summary>Disposes <paramref name="root"/> and its transitive <c>ChildOf</c> descendants (the
    /// instance's prefab-owned subtree), depth-first, bounded against a malformed cycle.</summary>
    private static void DisposeSubtree(Entity root, Dictionary<Entity, List<Entity>> childrenByParent)
    {
        var order = new List<Entity>();
        var seen = new HashSet<Entity>();
        var stack = new Stack<Entity>();
        stack.Push(root);
        while (stack.Count > 0 && order.Count < 100000)
        {
            var e = stack.Pop();
            if (!seen.Add(e)) continue;
            order.Add(e);
            if (childrenByParent.TryGetValue(e, out var children))
                foreach (var c in children) stack.Push(c);
        }

        foreach (var e in order)
            if (e.IsAlive) e.Dispose();
    }
}
