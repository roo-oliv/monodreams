#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.LevelEditor.Inspector;

/// <summary>
/// Pure builder for the editor's <b>entity scene tree</b> (the "Scene" panel section): from a flat
/// pool of candidate entities it produces a pre-order (DFS) list of <see cref="Node"/>s — roots
/// first, each entity's <c>ChildOfComponent</c> descendants nested one indent level deeper —
/// filtered by an <c>include</c> predicate. World-free logic (it only reads
/// <c>ChildOfComponent</c> off the entities it is handed), so it is directly unit-testable with a
/// hand-built entity pool.
///
/// <para><b>Editor-infrastructure hiding.</b> The caller supplies the candidate pool already
/// filtered to scene entities (the system uses <c>With&lt;TransformComponent&gt;
/// Without&lt;EditorInfrastructureComponent&gt;</c>), and/or an <c>include</c> predicate; an
/// entity for which <c>include</c> is false is never a node, and its children re-attach to their
/// nearest <b>included</b> ancestor (or become roots), so hiding an editor entity never orphans a
/// game child.</para>
/// </summary>
public static class EntitySceneTree
{
    /// <summary>One flattened tree row: the entity, its indentation depth (0 = root), and whether it
    /// has any included children (so the panel knows whether to draw a collapse arrow).</summary>
    public readonly struct Node
    {
        public readonly Entity Entity;
        public readonly int Depth;
        public readonly bool HasChildren;

        public Node(Entity entity, int depth, bool hasChildren)
        {
            Entity = entity;
            Depth = depth;
            HasChildren = hasChildren;
        }
    }

    /// <summary>
    /// Builds the pre-order tree from <paramref name="pool"/> (candidate entities in a stable order —
    /// the caller's creation-ordered set). An entity is a node only when <paramref name="include"/>
    /// returns true (default: everything alive); its parent is the nearest included ancestor along
    /// its <c>ChildOfComponent</c> chain, else it is a root. Roots and siblings keep
    /// <paramref name="pool"/> order (stable, intuitive creation order).
    /// </summary>
    public static List<Node> Build(IReadOnlyList<Entity> pool, Func<Entity, bool>? include = null)
    {
        include ??= static e => e.IsAlive;
        var result = new List<Node>();
        if (pool == null || pool.Count == 0) return result;

        // The included set + preserved order.
        var included = new HashSet<Entity>();
        var ordered = new List<Entity>(pool.Count);
        foreach (var e in pool)
        {
            if (!e.IsAlive || !include(e) || included.Contains(e)) continue;
            included.Add(e);
            ordered.Add(e);
        }
        if (ordered.Count == 0) return result;

        // Resolve each entity's effective parent = its nearest INCLUDED ancestor (skipping any
        // excluded links), and bucket children under it. Entities with no included ancestor are roots.
        var childrenOf = new Dictionary<Entity, List<Entity>>();
        var roots = new List<Entity>();
        foreach (var e in ordered)
        {
            var parent = NearestIncludedAncestor(e, included);
            if (parent is { } p)
            {
                if (!childrenOf.TryGetValue(p, out var list))
                    childrenOf[p] = list = new List<Entity>();
                list.Add(e);
            }
            else
            {
                roots.Add(e);
            }
        }

        // DFS pre-order from each root, preserving sibling (pool) order.
        foreach (var root in roots)
            Emit(root, 0, childrenOf, result);
        return result;
    }

    private static void Emit(Entity entity, int depth,
        Dictionary<Entity, List<Entity>> childrenOf, List<Node> result)
    {
        var hasChildren = childrenOf.TryGetValue(entity, out var kids) && kids.Count > 0;
        result.Add(new Node(entity, depth, hasChildren));
        if (!hasChildren) return;
        foreach (var child in kids!)
            Emit(child, depth + 1, childrenOf, result);
    }

    /// <summary>Walks <paramref name="entity"/>'s <c>ChildOfComponent</c> chain and returns the
    /// first ancestor in <paramref name="included"/>, or null if none (the entity is a root). A
    /// broken/dead parent link ends the walk (treated as no ancestor).</summary>
    private static Entity? NearestIncludedAncestor(Entity entity, HashSet<Entity> included)
    {
        var current = entity;
        // Bounded by the chain length; a malformed self-cycle is guarded by the visited set.
        var visited = new HashSet<Entity> { current };
        while (current.IsAlive && current.Has<ChildOfComponent>())
        {
            var parent = current.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive || !visited.Add(parent)) return null;
            if (included.Contains(parent)) return parent;
            current = parent;
        }
        return null;
    }
}
