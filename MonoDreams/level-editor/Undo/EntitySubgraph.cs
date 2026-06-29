#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// Collects the <c>ChildOfComponent</c> descendant closure of a root entity — the set delete must
/// snapshot so undo can restore the whole sub-graph (an entity plus its children, e.g. a player and
/// its orbiting orbs). The result is ordered roots-first / parent-before-children so the
/// <c>SceneSerializer</c>'s parent indices stay valid on a restore.
///
/// <para>This mirrors <c>SceneWriter.CollectMembership</c>'s descendant walk but is rooted at a
/// single given entity rather than every <c>SceneObjectComponent</c> tag — delete operates on the
/// entity the designer picked, not on the whole save set.</para>
/// </summary>
public static class EntitySubgraph
{
    /// <summary>Returns <paramref name="root"/> plus its transitive <c>ChildOf</c> descendants,
    /// parent-before-children. Returns just the root if it has no children.</summary>
    public static List<Entity> Collect(World world, Entity root)
    {
        var childrenByParent = new Dictionary<Entity, List<Entity>>();
        using var childSet = world.GetEntities().With<ChildOfComponent>().AsSet();
        foreach (var entity in childSet.GetEntities())
        {
            var parent = entity.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) continue;
            if (!childrenByParent.TryGetValue(parent, out var list))
                childrenByParent[parent] = list = new List<Entity>();
            list.Add(entity);
        }

        var result = new List<Entity>();
        var seen = new HashSet<Entity>();
        AddWithDescendants(root, childrenByParent, result, seen);
        return result;
    }

    private static void AddWithDescendants(
        Entity entity,
        Dictionary<Entity, List<Entity>> childrenByParent,
        List<Entity> result,
        HashSet<Entity> seen)
    {
        if (!seen.Add(entity)) return;
        result.Add(entity);
        if (!childrenByParent.TryGetValue(entity, out var children)) return;
        foreach (var child in children)
            AddWithDescendants(child, childrenByParent, result, seen);
    }
}
