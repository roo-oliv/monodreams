using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.Util;

namespace MonoDreams.System.Collision;

public class CollisionResolutionSystem<TCollidableComponent, TPositionComponent, TVelocityComponent> : ISystem<GameState>
    where TCollidableComponent : BoxCollider, ICollider
    where TPositionComponent : Position
    where TVelocityComponent : Velocity
{
    private readonly World _world;
    private readonly List<CollisionMessage> _collisions;
        
    public CollisionResolutionSystem(World world)
    {
        _world = world;
        world.Subscribe(this);
        _collisions = [];
    }

    [Subscribe]
    private void On(in CollisionMessage message) => _collisions.Add(message);
        
    public bool IsEnabled { get; set; } = true;
        
    public void Dispose()
    {
        _collisions.Clear();
        GC.SuppressFinalize(this);
    }

    public void Update(GameState state)
    {
        _collisions.Sort((l, r) => l.ContactTime.CompareTo(r.ContactTime));
        foreach (var collision in _collisions)
        {
            ResolveCollision(collision);
        }
        _collisions.Clear();
    }

    private void ResolveCollision(CollisionMessage collision)
    {
        var entity = collision.BaseEntity;
        var position = entity.Get<TPositionComponent>();
        var dynamicRect = entity.Get<TCollidableComponent>().Bounds.AtPosition(position.Current);
        
        var collidingEntity = collision.CollidingEntity;
        var targetPosition = collidingEntity.Get<TPositionComponent>();
        var targetRect = collidingEntity.Get<TCollidableComponent>().Bounds.AtPosition(targetPosition.Current);
        
        if (!CollisionDetectionSystem.DynamicRectVsRect(dynamicRect, position.Delta, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime)) return;
        if (contactNormal.X != 0)
        {
            position.Current.X = contactPoint.X - dynamicRect.Width / 2f;

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
            position.Current.Y = contactPoint.Y - dynamicRect.Height / 2f;
            
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

public class CollisionResolutionSystem<TPosition, TVelocity>(World world)
    : CollisionResolutionSystem<BoxCollider, TPosition, TVelocity>(world)
    where TPosition : Position
    where TVelocity : Velocity;

public class CollisionResolutionSystem(World world)
    : CollisionResolutionSystem<BoxCollider, Position, Velocity>(world);

