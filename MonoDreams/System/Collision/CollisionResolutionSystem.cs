using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System.Collision;

public class CollisionResolutionSystem<TCollidableComponent, TPositionComponent> : ISystem<GameState>
    where TCollidableComponent : BoxCollidable, ICollidable
    where TPositionComponent : Position
{
    private readonly List<CollisionMessage> _collisions;
        
    public CollisionResolutionSystem(World world)
    {
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

    private static void ResolveCollision(CollisionMessage collision)
    {
        var position = collision.BaseEntity.Get<TPositionComponent>();
        var dynamicRect = collision.BaseEntity.Get<TCollidableComponent>().Bounds.AtPosition(position.Current);
        var targetPosition = collision.CollidingEntity.Get<TPositionComponent>();
        var targetRect = collision.CollidingEntity.Get<TCollidableComponent>().Bounds.AtPosition(targetPosition.Current);
        if (!CollisionDetectionSystem.DynamicRectVsRect(dynamicRect, position.Delta, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime)) return;
        if (contactNormal.X != 0)
        {
            position.Current.X = contactPoint.X - dynamicRect.Width / 2f;
        }

        if (contactNormal.Y != 0)
        {
            position.Current.Y = contactPoint.Y - dynamicRect.Height / 2f;
        }
    }
}

public class CollisionResolutionSystem<TPosition>(World world)
    : CollisionResolutionSystem<BoxCollidable, TPosition>(world)
    where TPosition : Position;

public class CollisionResolutionSystem(World world)
    : CollisionResolutionSystem<BoxCollidable, Position>(world);

