using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.Message;

namespace MonoDreams.Examples.Message;

public readonly record struct CollisionMessage(
    Entity BaseEntity,
    Entity CollidingEntity,
    Vector2 ContactPoint,
    Vector2 ContactNormal,
    float ContactTime,
    int Layer = -1,
    CollisionType Type = CollisionType.Generic) : ICollisionMessage
{
    public static CollisionMessage Create(
        Entity entity,
        Entity target,
        Vector2 contactPoint,
        Vector2 contactNormal,
        float contactTime,
        int layer)
    {
        CollisionType collisionType = DetermineCollisionType(entity, target);
        // Console.WriteLine($"Collision detected: {entity} vs {target} at {contactPoint} (contact normal: {contactNormal}| time: {contactTime})");
        return new CollisionMessage(entity, target, contactPoint, contactNormal, contactTime, layer, collisionType);
    }

    private static CollisionType DetermineCollisionType(Entity entity, Entity target)
    {
        var entityType = entity.Get<EntityInfo>().Type;
        var targetType = target.Get<EntityInfo>().Type;
        return entityType switch
        {
            EntityType.Player when targetType == EntityType.Collectible => CollisionType.Collectible,
            EntityType.Player when targetType == EntityType.Projectile => CollisionType.Damage,
            EntityType.Player when targetType == EntityType.Zone => CollisionType.Dialogue,
            _ => CollisionType.Physics
        };
    }
}

public enum CollisionType
{
    Generic,
    Physics,
    Collectible,
    Damage,
    Dialogue
}