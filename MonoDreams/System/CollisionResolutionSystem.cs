using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public class CollisionResolutionSystem : ISystem<GameState>
{
    private readonly List<CollisionMessage> _collisions;
        
    public CollisionResolutionSystem(World world)
    {
        world.Subscribe(this);
        _collisions = new List<CollisionMessage>();
    }

    [Subscribe]
    private void On(in CollisionMessage message) => _collisions.Add(message);
        
    public bool IsEnabled { get; set; } = true;
        
    public void Dispose()
    {
        _collisions.Clear();
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
        var entity = collision.BaseEntity;
        ref var dynamicRect = ref entity.Get<DrawInfo>().Destination;
        ref var body = ref entity.Get<DynamicBody>();
        var displacement = body.NextPosition - body.CurrentPosition;
        ref var targetRect = ref collision.CollidingEntity.Get<DrawInfo>().Destination;
        if (!CollisionDetectionSystem.DynamicRectVsRect(dynamicRect, displacement, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime)) return;
        if (contactNormal.X != 0)
        {
            body.NextPosition.X = contactPoint.X - dynamicRect.Width / 2f;
            body.CurrentPosition.X = body.NextPosition.X;
        }

        if (contactNormal.Y != 0)
        {
            body.NextPosition.Y = contactPoint.Y - dynamicRect.Height / 2f;
            body.CurrentPosition.Y = body.NextPosition.Y;
            body.IsRiding |= contactNormal.Y < 0;
        }
    }
}