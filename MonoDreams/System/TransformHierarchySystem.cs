using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.State;
using System.Collections.Generic;

namespace MonoDreams.System
{
    /// <summary>
    /// Handles Transform hierarchy updates by propagating dirty flags from parents to children.
    /// This system must run BEFORE any systems that read world transforms to ensure proper propagation.
    /// </summary>
    public class TransformHierarchySystem : AEntitySetSystem<GameState>
    {
        private readonly Dictionary<Transform, List<Entity>> _parentToChildren = new();

        public TransformHierarchySystem(World world)
            : base(world.GetEntities().With<Transform>().AsSet())
        {
        }

        protected override void PreUpdate(GameState state)
        {
            // Rebuild parent-child mapping
            _parentToChildren.Clear();

            foreach (var entity in Set.GetEntities())
            {
                ref var transform = ref entity.Get<Transform>();

                if (transform.Parent != null)
                {
                    var parent = transform.Parent;

                    if (!_parentToChildren.ContainsKey(parent))
                    {
                        _parentToChildren[parent] = new List<Entity>();
                    }

                    _parentToChildren[parent].Add(entity);
                }
            }
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref var transform = ref entity.Get<Transform>();

            // If this transform is dirty and has children, propagate the dirty flag
            if (transform.IsDirty && _parentToChildren.ContainsKey(transform))
            {
                PropagateIsDirtyToChildren(transform);
            }
        }

        private void PropagateIsDirtyToChildren(Transform parent)
        {
            if (!_parentToChildren.ContainsKey(parent))
                return;

            foreach (var child in _parentToChildren[parent])
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
    }
}
