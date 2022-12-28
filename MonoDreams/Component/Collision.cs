using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public struct Collision
{
    public readonly Entity CollidingEntity;
    public Vector2 ContactPoint;
    public Vector2 ContactNormal;
    public float ContactTime;

    public Collision(in Entity collidingEntity, Vector2 contactPoint, Vector2 contactNormal, float contactTime)
    {
        CollidingEntity = collidingEntity;
        ContactPoint = contactPoint;
        ContactNormal = contactNormal;
        ContactTime = contactTime;
    }
}