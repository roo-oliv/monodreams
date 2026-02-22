using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.Extension;

/// <summary>
/// Extension methods for setting up entity parent-child relationships via ChildOf.
/// </summary>
public static class EntityHierarchyExtensions
{
    /// <summary>
    /// Sets a structural parent-child relationship. Also syncs Transform.Parent if both entities have a Transform.
    /// </summary>
    public static void SetParent(this Entity child, Entity parent)
    {
        child.Set(new ChildOf(parent));

        // Eagerly sync Transform.Parent if both have Transform
        if (child.Has<Transform>() && parent.Has<Transform>())
        {
            var parentTransform = parent.Get<Transform>();
            ref var childTransform = ref child.Get<Transform>();
            childTransform.Parent = parentTransform;
        }
    }

    /// <summary>
    /// Removes the structural parent-child relationship. Snapshots the world position before detaching
    /// so the entity stays in place visually.
    /// </summary>
    public static void RemoveParent(this Entity child)
    {
        if (!child.Has<ChildOf>()) return;

        // Snapshot world position before detaching
        if (child.Has<Transform>())
        {
            ref var childTransform = ref child.Get<Transform>();
            if (childTransform.Parent != null)
            {
                var worldPos = childTransform.WorldPosition;
                childTransform.Parent = null;
                childTransform.Position = worldPos;
            }
        }

        child.Remove<ChildOf>();
    }
}
