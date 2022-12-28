using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using NotImplementedException = System.NotImplementedException;

namespace MonoDreams.Message;

public readonly struct CollisionMessage
{
    public readonly Entity BaseEntity;
    public readonly Entity CollidingEntity;
    public readonly Vector2 ContactPoint;
    public readonly Vector2 ContactNormal;
    public readonly float ContactTime;

    public CollisionMessage(in Entity baseEntity, in Entity collidingEntity, Vector2 contactPoint, Vector2 contactNormal, float contactTime)
    {
        BaseEntity = baseEntity;
        CollidingEntity = collidingEntity;
        ContactPoint = contactPoint;
        ContactNormal = contactNormal;
        ContactTime = contactTime;
    }
}