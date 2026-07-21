using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.System.Collision;

/// <summary>
/// Factory the detection system calls to build a (game-extensible) collision message. Detection
/// supplies the two collider entities AND their resolved bodies (via <see cref="ColliderBody"/>), so
/// a game delegate can classify by identity (collider) or physics (body) without re-walking the
/// hierarchy. See <c>CollisionMessage</c> for the A=initiator / B=other convention.
/// </summary>
public delegate TCollisionMessage CreateCollisionMessageDelegate<out TCollisionMessage>(
    Entity colliderA,
    Entity colliderB,
    Entity bodyA,
    Entity bodyB,
    Vector2 contactPoint,
    Vector2 contactNormal,
    float contactTime,
    float penetrationDepth,
    int layer);
