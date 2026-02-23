using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.Message;

public readonly record struct CollisionMessage(
    Entity BaseEntity,
    Entity CollidingEntity,
    Vector2 ContactPoint,
    Vector2 ContactNormal,
    float ContactTime,
    float PenetrationDepth = 0f,
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
        return new CollisionMessage(entity, target, contactPoint, contactNormal, contactTime, 0f, layer, CollisionType.Physics);
    }

    public static CollisionMessage CreateSAT(
        Entity entity,
        Entity target,
        Vector2 contactPoint,
        Vector2 contactNormal,
        float penetrationDepth,
        int layer)
    {
        return new CollisionMessage(entity, target, contactPoint, contactNormal, 0f, penetrationDepth, layer, CollisionType.Physics);
    }
}

public enum CollisionType
{
    Generic,
    Physics,
    Collectible,
    Damage,
    Dialogue,
    NPCInteraction
}
