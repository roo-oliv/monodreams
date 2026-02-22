using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.State;

/// <summary>
/// System-maintained lookup for entity parent-child relationships.
/// Rebuilt each frame by HierarchySystem from ChildOf components.
/// </summary>
public class EntityHierarchy
{
    private readonly Dictionary<Entity, List<Entity>> _childrenMap = new();
    private readonly Dictionary<Entity, Entity> _parentMap = new();

    /// <summary>
    /// Returns the children of an entity, or an empty list if it has none.
    /// </summary>
    public IReadOnlyList<Entity> GetChildren(Entity parent)
    {
        return _childrenMap.TryGetValue(parent, out var children)
            ? children
            : (IReadOnlyList<Entity>)[];
    }

    /// <summary>
    /// Returns the parent of an entity, or null if it has no parent.
    /// </summary>
    public Entity? GetParent(Entity child)
    {
        return _parentMap.TryGetValue(child, out var parent) ? parent : null;
    }

    /// <summary>
    /// Returns true if the entity has any children.
    /// </summary>
    public bool HasChildren(Entity parent)
    {
        return _childrenMap.TryGetValue(parent, out var children) && children.Count > 0;
    }

    /// <summary>
    /// Returns entities from the given set that do not have a ChildOf component (i.e., root entities).
    /// </summary>
    public IEnumerable<Entity> GetRoots(IEnumerable<Entity> entities)
    {
        return entities.Where(e => e.IsAlive && !_parentMap.ContainsKey(e));
    }

    /// <summary>
    /// Rebuilds the hierarchy from the given entity set. Called by HierarchySystem each frame.
    /// </summary>
    internal void Rebuild(ReadOnlySpan<Entity> entities)
    {
        _childrenMap.Clear();
        _parentMap.Clear();

        foreach (var entity in entities)
        {
            if (!entity.IsAlive || !entity.Has<ChildOf>()) continue;

            var parent = entity.Get<ChildOf>().Parent;
            if (!parent.IsAlive) continue;

            _parentMap[entity] = parent;

            if (!_childrenMap.TryGetValue(parent, out var children))
            {
                children = new List<Entity>();
                _childrenMap[parent] = children;
            }

            children.Add(entity);
        }
    }
}
