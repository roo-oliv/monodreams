using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

/// <summary>
/// Maintains the EntityHierarchy resource, syncs Transform.Parent from ChildOf,
/// propagates dirty flags from parent to child transforms, and cascade-disposes
/// orphaned children (whose parent entity is no longer alive).
///
/// Replaces TransformHierarchySystem. Must run AFTER logic systems modify transforms
/// but BEFORE any systems that read world transforms (camera, rendering).
/// </summary>
public class HierarchySystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EntitySet _childOfSet;
    private readonly EntitySet _transformSet;
    private readonly EntityHierarchy _hierarchy;
    private readonly Dictionary<Transform, List<Entity>> _parentToChildren = new();

    public bool IsEnabled { get; set; } = true;

    public HierarchySystem(World world)
    {
        _world = world;
        _childOfSet = world.GetEntities().With<ChildOf>().AsSet();
        _transformSet = world.GetEntities().With<Transform>().AsSet();

        // Register the hierarchy as a managed resource on the world
        _hierarchy = new EntityHierarchy();
        world.Set(_hierarchy);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Step 1: Detect orphans and cascade-dispose them
        DisposeOrphans();

        // Step 2: Rebuild the EntityHierarchy lookup from current ChildOf components
        _hierarchy.Rebuild(_childOfSet.GetEntities());

        // Step 3: Sync Transform.Parent from ChildOf for entities that have both
        SyncTransformParents();

        // Step 4: Propagate dirty flags from parent to children (same as old TransformHierarchySystem)
        PropagateDirtyFlags();
    }

    private void DisposeOrphans()
    {
        var entities = _childOfSet.GetEntities();
        // Collect orphans first to avoid modifying the set during iteration
        List<Entity> orphans = null;

        foreach (var entity in entities)
        {
            if (!entity.IsAlive) continue;
            var parent = entity.Get<ChildOf>().Parent;
            if (!parent.IsAlive)
            {
                orphans ??= new List<Entity>();
                orphans.Add(entity);
            }
        }

        if (orphans != null)
        {
            foreach (var orphan in orphans)
            {
                if (orphan.IsAlive)
                {
                    Logger.Debug($"Cascade-disposing orphan entity (parent destroyed)");
                    orphan.Dispose();
                }
            }
        }
    }

    private void SyncTransformParents()
    {
        foreach (var entity in _childOfSet.GetEntities())
        {
            if (!entity.IsAlive || !entity.Has<Transform>()) continue;

            var parentEntity = entity.Get<ChildOf>().Parent;
            if (!parentEntity.IsAlive || !parentEntity.Has<Transform>()) continue;

            ref var childTransform = ref entity.Get<Transform>();
            var parentTransform = parentEntity.Get<Transform>();

            // Only sync if Transform.Parent doesn't already point to the right transform
            if (childTransform.Parent != parentTransform)
            {
                childTransform.Parent = parentTransform;
            }
        }
    }

    private void PropagateDirtyFlags()
    {
        // Rebuild parent-to-children transform mapping
        _parentToChildren.Clear();

        foreach (var entity in _transformSet.GetEntities())
        {
            ref var transform = ref entity.Get<Transform>();
            if (transform.Parent != null)
            {
                if (!_parentToChildren.TryGetValue(transform.Parent, out var children))
                {
                    children = new List<Entity>();
                    _parentToChildren[transform.Parent] = children;
                }
                children.Add(entity);
            }
        }

        // Propagate dirty flags
        foreach (var entity in _transformSet.GetEntities())
        {
            ref var transform = ref entity.Get<Transform>();
            if (transform.IsDirty && _parentToChildren.ContainsKey(transform))
            {
                PropagateIsDirtyToChildren(transform);
            }
        }
    }

    private void PropagateIsDirtyToChildren(Transform parent)
    {
        if (!_parentToChildren.TryGetValue(parent, out var children))
            return;

        foreach (var child in children)
        {
            if (child.IsAlive && child.Has<Transform>())
            {
                ref var childTransform = ref child.Get<Transform>();
                childTransform.SetDirty();

                // Recursively propagate to grandchildren
                PropagateIsDirtyToChildren(childTransform);
            }
        }
    }

    public void Dispose()
    {
        _childOfSet.Dispose();
        _transformSet.Dispose();
    }
}
