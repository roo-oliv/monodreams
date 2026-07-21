using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.Message;

/// <summary>
/// The contact contract between detection and consumers under the colliders-as-entities model.
/// Carries BOTH granularities: the two collider entities (<see cref="ColliderA"/>/<see cref="ColliderB"/>
/// — identity consumers like dialogue zones read here, where the shape + its <c>EntityInfoComponent</c>
/// live) and the two resolved bodies (<see cref="BodyA"/>/<see cref="BodyB"/> — physics/resolution
/// consumers read here; resolution write-back targets the body's Transform/Velocity). A is the
/// initiator (the active, non-passive mover); B is the other side.
/// </summary>
public readonly record struct CollisionMessage(
    Entity ColliderA,
    Entity ColliderB,
    Entity BodyA,
    Entity BodyB,
    Vector2 ContactPoint,
    Vector2 ContactNormal,
    float ContactTime,
    float PenetrationDepth = 0f,
    int Layer = -1,
    CollisionType Type = CollisionType.Generic) : ICollisionMessage;

public enum CollisionType
{
    Generic,
    Physics,
    Collectible,
    Damage,
    Dialogue,
    NPCInteraction
}
