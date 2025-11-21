using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using FlecsEntity = Flecs.NET.Core.Entity;

namespace MonoDreams.Examples.Message;

/// <summary>
/// Flecs collision message storing entity IDs and collision data.
/// </summary>
public readonly record struct CollisionMessage(
    ulong BaseEntityId,
    ulong CollidingEntityId,
    Vector2 ContactPoint,
    Vector2 ContactNormal,
    float ContactTime,
    int Layer = -1,
    CollisionType Type = CollisionType.Generic)
{
    // Factory method for Flecs entities
    public static CollisionMessage Create(
        FlecsEntity entity,
        FlecsEntity target,
        Vector2 contactPoint,
        Vector2 contactNormal,
        float contactTime,
        int layer)
    {
        CollisionType collisionType = DetermineCollisionType(entity, target);
        return new CollisionMessage(
            entity.Id.Value,
            target.Id.Value,
            contactPoint,
            contactNormal,
            contactTime,
            layer,
            collisionType);
    }

    private static CollisionType DetermineCollisionType(FlecsEntity entity, FlecsEntity target)
    {
        // For Flecs entities, check if they have EntityInfo component
        if (!entity.Has<EntityInfo>() || !target.Has<EntityInfo>())
            return CollisionType.Physics;

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
