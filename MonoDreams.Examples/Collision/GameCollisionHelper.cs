using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Message;

namespace MonoDreams.Examples.Collision;

/// <summary>
/// Game-specific collision type determination for the platformer levels.
/// Replaces the logic that was previously in core CollisionMessage.DetermineCollisionType.
/// </summary>
public static class GameCollisionHelper
{
    public static CollisionMessage Create(
        Entity entity,
        Entity target,
        Vector2 contactPoint,
        Vector2 contactNormal,
        float contactTime,
        int layer)
    {
        var collisionType = DetermineCollisionType(entity, target);
        return new CollisionMessage(entity, target, contactPoint, contactNormal, contactTime, layer, collisionType);
    }

    private static CollisionType DetermineCollisionType(Entity entity, Entity target)
    {
        var entityType = entity.Get<EntityInfo>().Type;
        var targetType = target.Get<EntityInfo>().Type;
        return entityType switch
        {
            "Player" when targetType == "Collectible" => CollisionType.Collectible,
            "Player" when targetType == "Projectile" => CollisionType.Damage,
            "Player" when targetType == "Zone" => CollisionType.Dialogue,
            "Player" when targetType == "NPCZone" => CollisionType.NPCInteraction,
            _ => CollisionType.Physics
        };
    }
}
