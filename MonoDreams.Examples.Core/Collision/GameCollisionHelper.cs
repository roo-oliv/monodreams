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
        Entity colliderA,
        Entity colliderB,
        Entity bodyA,
        Entity bodyB,
        Vector2 contactPoint,
        Vector2 contactNormal,
        float contactTime,
        float penetrationDepth,
        int layer)
    {
        var collisionType = DetermineCollisionType(IdentityType(colliderA, bodyA), IdentityType(colliderB, bodyB));
        return new CollisionMessage(colliderA, colliderB, bodyA, bodyB, contactPoint, contactNormal, contactTime, penetrationDepth, layer, collisionType);
    }

    // The game-object identity a collider represents: it lives ON the collider entity for standalone
    // identity colliders (dialogue/NPC zones), and on the BODY for a physics entity whose collider is
    // a child (player). Read the collider first, then fall back to the body.
    private static string IdentityType(Entity collider, Entity body)
    {
        if (collider.IsAlive && collider.Has<EntityInfoComponent>()) return collider.Get<EntityInfoComponent>().Type;
        if (body.IsAlive && body.Has<EntityInfoComponent>()) return body.Get<EntityInfoComponent>().Type;
        return null;
    }

    private static CollisionType DetermineCollisionType(string entityType, string targetType)
    {
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
