using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

/// <summary>
/// Maintains the EntityHierarchy resource, syncs TransformComponent.Parent from ChildOfComponent,
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
    private readonly Dictionary<TransformComponent, List<Entity>> _parentToChildren = new();

    public bool IsEnabled { get; set; } = true;

    public HierarchySystem(World world)
    {
        _world = world;
        _childOfSet = world.GetEntities().With<ChildOfComponent>().AsSet();
        _transformSet = world.GetEntities().With<TransformComponent>().AsSet();

        // Register the hierarchy as a managed resource on the world
        _hierarchy = new EntityHierarchy();
        world.Set(_hierarchy);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Step 1: Detect orphans and cascade-dispose them
        DisposeOrphans();

        // Step 2: Rebuild the EntityHierarchy lookup from current ChildOfComponent components
        _hierarchy.Rebuild(_childOfSet.GetEntities());

        // Step 3: Sync TransformComponent.Parent from ChildOfComponent for entities that have both
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
            var parent = entity.Get<ChildOfComponent>().Parent;
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
            if (!entity.IsAlive || !entity.Has<TransformComponent>()) continue;

            var parentEntity = entity.Get<ChildOfComponent>().Parent;
            if (!parentEntity.IsAlive || !parentEntity.Has<TransformComponent>()) continue;

            ref var childTransform = ref entity.Get<TransformComponent>();
            var parentTransform = parentEntity.Get<TransformComponent>();

            // Only sync if TransformComponent.Parent doesn't already point to the right transform
            if (childTransform.Parent != parentTransform)
            {
                childTransform.Parent = parentTransform;
            }
        }
    }

    private void PropagateDirtyFlags()
    {
        // Clear list contents but keep the lists allocated to reduce GC pressure
        foreach (var list in _parentToChildren.Values)
            list.Clear();

        foreach (var entity in _transformSet.GetEntities())
        {
            ref var transform = ref entity.Get<TransformComponent>();
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

        // Propagate dirty flags. Key off NeedsHierarchyUpdate — NOT IsDirty — because IsDirty is
        // cleared as a side effect of any WorldMatrix read: a reader running between an edit and this
        // pass (e.g. ButtonMeshPrepSystem reading a moved button's WorldPosition before HierarchySystem)
        // would otherwise erase the signal and leave descendants stale (the gizmo-vs-modal divergence).
        // Snapshot the changed roots first so children re-flagged during propagation don't add roots
        // mid-pass, then clear every transform's propagation flag for the next frame.
        List<TransformComponent> changedRoots = null;
        foreach (var entity in _transformSet.GetEntities())
        {
            ref var transform = ref entity.Get<TransformComponent>();
            if (transform.NeedsHierarchyUpdate && _parentToChildren.ContainsKey(transform))
            {
                (changedRoots ??= new List<TransformComponent>()).Add(transform);
            }
        }

        if (changedRoots != null)
        {
            foreach (var root in changedRoots)
                PropagateIsDirtyToChildren(root);
        }

        foreach (var entity in _transformSet.GetEntities())
            entity.Get<TransformComponent>().ClearHierarchyDirty();
    }

    private void PropagateIsDirtyToChildren(TransformComponent parent)
    {
        if (!_parentToChildren.TryGetValue(parent, out var children))
            return;

        foreach (var child in children)
        {
            if (child.IsAlive && child.Has<TransformComponent>())
            {
                ref var childTransform = ref child.Get<TransformComponent>();
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
