using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.Util;

namespace MonoDreams.Examples.System.Collision;

/// <summary>
/// Collision resolution system that works with Transform component instead of Position.
/// </summary>
public class TransformCollisionResolutionSystem<TCollisionMessage> : ISystem<GameState>
    where TCollisionMessage : ICollisionMessage
{
    private readonly World _world;
    protected readonly List<TCollisionMessage> Collisions;

    public TransformCollisionResolutionSystem(World world)
    {
        _world = world;
        world.Subscribe(this);
        Collisions = [];
    }

    [Subscribe]
    protected virtual void On(in TCollisionMessage message) => Collisions.Add(message);

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        Collisions.Clear();
        GC.SuppressFinalize(this);
    }

    public void Update(GameState state)
    {
        Collisions.Sort((l, r) => l.ContactTime.CompareTo(r.ContactTime));
        foreach (var collision in Collisions)
        {
            ResolveCollision(collision);
        }
        Collisions.Clear();
    }

    private void ResolveCollision(TCollisionMessage collision)
    {
        var entity = collision.BaseEntity;
        ref var transform = ref entity.Get<Transform>();
        var collidable = entity.Get<BoxCollider>();
        var dynamicRect = collidable.Bounds.AtPosition(transform.Position);

        var collidingEntity = collision.CollidingEntity;
        var targetTransform = collidingEntity.Get<Transform>();
        var targetRect = collidingEntity.Get<BoxCollider>().Bounds.AtPosition(targetTransform.Position);

        if (!TransformCollisionDetectionSystem<TCollisionMessage>.DynamicRectVsRect(dynamicRect, transform.Delta, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime)) return;

        if (contactNormal.X != 0)
        {
            transform.SetPositionX(contactPoint.X - dynamicRect.Width / 2f - collidable.Bounds.Location.X);

            if (entity.Has<Velocity>())
            {
                ref var velocity = ref entity.Get<Velocity>();
                velocity.Current.X = 0;
            }

            if ((int)Math.Abs(contactPoint.X + dynamicRect.Width / 2f - targetRect.Left) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(collidingEntity, RelativeReferential.Left));
            }
            else if ((int)Math.Abs(contactPoint.X - dynamicRect.Width / 2f - targetRect.Right) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(collidingEntity, RelativeReferential.Right));
            }
        }

        if (contactNormal.Y != 0)
        {
            transform.SetPositionY(contactPoint.Y - dynamicRect.Height / 2f - collidable.Bounds.Location.Y);

            if (entity.Has<Velocity>())
            {
                ref var velocity = ref entity.Get<Velocity>();
                velocity.Current.Y = 0;
            }

            if ((int)Math.Abs(contactPoint.Y + dynamicRect.Height / 2f - targetRect.Top) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(collidingEntity, RelativeReferential.Top));
            }
            else if ((int)Math.Abs(contactPoint.Y - dynamicRect.Height / 2f - targetRect.Bottom) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(collidingEntity, RelativeReferential.Bottom));
            }
        }
    }
}
