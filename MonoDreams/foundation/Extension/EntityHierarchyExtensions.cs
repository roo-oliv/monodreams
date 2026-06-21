using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.Extension;

/// <summary>
/// Extension methods for setting up entity parent-child relationships via ChildOfComponent.
/// </summary>
public static class EntityHierarchyExtensions
{
    /// <summary>
    /// Sets a structural parent-child relationship. Also syncs TransformComponent.Parent if both entities have a TransformComponent.
    /// </summary>
    public static void SetParent(this Entity child, Entity parent)
    {
        child.Set(new ChildOfComponent(parent));

        // Eagerly sync TransformComponent.Parent if both have TransformComponent
        if (child.Has<TransformComponent>() && parent.Has<TransformComponent>())
        {
            var parentTransform = parent.Get<TransformComponent>();
            ref var childTransform = ref child.Get<TransformComponent>();
            childTransform.Parent = parentTransform;
        }
    }

    /// <summary>
    /// Removes the structural parent-child relationship. Snapshots the world position before detaching
    /// so the entity stays in place visually.
    /// </summary>
    public static void RemoveParent(this Entity child)
    {
        if (!child.Has<ChildOfComponent>()) return;

        // Snapshot world position before detaching
        if (child.Has<TransformComponent>())
        {
            ref var childTransform = ref child.Get<TransformComponent>();
            if (childTransform.Parent != null)
            {
                var worldPos = childTransform.WorldPosition;
                childTransform.Parent = null;
                childTransform.Position = worldPos;
            }
        }

        child.Remove<ChildOfComponent>();
    }
}
