using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.Util;

namespace MonoDreams.System.Collision;

public class CollisionResolutionSystem<TCollidableComponent, TPositionComponent, TVelocityComponent, TCollisionMessage>
    : ISystem<GameState>
    where TCollidableComponent : BoxCollider, ICollider
    where TPositionComponent : Position
    where TVelocityComponent : Velocity
    where TCollisionMessage : ICollisionMessage
{
    private readonly World _world;
    protected readonly List<TCollisionMessage> Collisions;

    public CollisionResolutionSystem(World world)
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
        var position = entity.Get<TPositionComponent>();
        var collidable = entity.Get<TCollidableComponent>();
        var dynamicRect = CollisionRect.FromBounds(collidable.Bounds, position.Current);

        var collidingEntity = collision.CollidingEntity;
        var targetPosition = collidingEntity.Get<TPositionComponent>();
        var targetRect = CollisionRect.FromBounds(collidingEntity.Get<TCollidableComponent>().Bounds, targetPosition.Current);

        if (!CollisionDetectionSystem<TCollisionMessage>.DynamicRectVsRect(dynamicRect, position.Delta, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime)) return;
        if (contactNormal.X != 0)
        {
            position.Current.X = contactPoint.X - dynamicRect.Width / 2f - collidable.Bounds.Location.X;

            if (entity.Has<TVelocityComponent>())
            {
                var velocity = entity.Get<TVelocityComponent>();
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
            position.Current.Y = contactPoint.Y - dynamicRect.Height / 2f - collidable.Bounds.Location.Y;

            if (entity.Has<TVelocityComponent>())
            {
                var velocity = entity.Get<TVelocityComponent>();
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

public class CollisionResolutionSystem<TPosition, TVelocity, TCollisionMessage>(World world)
    : CollisionResolutionSystem<BoxCollider, TPosition, TVelocity, TCollisionMessage>(world)
    where TPosition : Position
    where TVelocity : Velocity
    where TCollisionMessage : ICollisionMessage;

public class CollisionResolutionSystem<TCollisionMessage>(World world)
    : CollisionResolutionSystem<BoxCollider, Position, Velocity, TCollisionMessage>(world)
    where TCollisionMessage : ICollisionMessage;
