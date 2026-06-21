using DefaultEcs;

namespace MonoDreams.Component;

/// <summary>
/// Structural parent-child relationship between entities.
/// Use for ownership hierarchy (NPC → zone, Player → orbs) where destroying the parent
/// should cascade to children and children should appear nested in the inspector.
/// For temporary spatial attachment (platform riding), use TransformComponent.Parent directly.
/// </summary>
public struct ChildOfComponent
{
    public Entity Parent;

    public ChildOfComponent(Entity parent)
    {
        Parent = parent;
    }
}
