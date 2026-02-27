using System;
using System.Collections.Generic;
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

namespace MonoDreams.System.Collision;

/// <summary>
/// Collision resolution system supporting both BoxCollider (swept AABB) and ConvexCollider (SAT).
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
        var collidingEntity = collision.CollidingEntity;

        var hasBoxA = entity.Has<BoxCollider>();
        var hasBoxB = collidingEntity.Has<BoxCollider>();

        if (hasBoxA && hasBoxB)
        {
            ResolveBoxVsBox(collision, entity, collidingEntity);
        }
        else
        {
            ResolveSAT(collision, entity, collidingEntity, hasBoxA, hasBoxB);
        }
    }

    private void ResolveBoxVsBox(TCollisionMessage collision, Entity entity, Entity collidingEntity)
    {
        ref var transform = ref entity.Get<Transform>();
        var collidable = entity.Get<BoxCollider>();
        var dynamicRect = CollisionRect.FromBounds(collidable.Bounds, transform.Position);

        var targetTransform = collidingEntity.Get<Transform>();
        var targetRect = CollisionRect.FromBounds(collidingEntity.Get<BoxCollider>().Bounds, targetTransform.Position);

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

    // Reusable buffers for box-to-polygon conversion
    private readonly Vector2[] _boxBufA = new Vector2[4];
    private readonly Vector2[] _boxBufB = new Vector2[4];

    private void ResolveSAT(TCollisionMessage collision, Entity entity, Entity collidingEntity, bool hasBoxA, bool hasBoxB)
    {
        ref var transform = ref entity.Get<Transform>();

        // Build current polygons
        Vector2[] polyA;
        Vector2[] polyB;

        if (hasBoxA)
        {
            SATCollision.BoxToPolygon(entity.Get<BoxCollider>(), transform, _boxBufA);
            polyA = _boxBufA;
        }
        else
        {
            var convex = entity.Get<ConvexCollider>();
            convex.UpdateWorldVertices(transform);
            polyA = convex.WorldVertices;
        }

        if (hasBoxB)
        {
            SATCollision.BoxToPolygon(collidingEntity.Get<BoxCollider>(), collidingEntity.Get<Transform>(), _boxBufB);
            polyB = _boxBufB;
        }
        else
        {
            polyB = collidingEntity.Get<ConvexCollider>().WorldVertices;
        }

        // Re-validate with current positions
        if (!SATCollision.PolygonVsPolygon(polyA, polyB, out var contactNormal, out var penetrationDepth)) return;

        // Push entity out by MTV
        transform.Translate(-contactNormal * penetrationDepth);

        // Velocity damping: remove velocity component moving into the collision
        if (entity.Has<Velocity>())
        {
            ref var velocity = ref entity.Get<Velocity>();
            var velDotNormal = Vector2.Dot(velocity.Current, contactNormal);
            if (velDotNormal > 0)
            {
                velocity.Current -= velDotNormal * contactNormal;
            }
        }

        // Derive side from contact normal direction for RigidBodyTouchMessage
        var side = NormalToSide(contactNormal);
        if (side.HasValue)
        {
            _world.Publish(new RigidBodyTouchMessage(collidingEntity, side.Value));
        }
    }

    private static RelativeReferential? NormalToSide(Vector2 normal)
    {
        // Use predominant axis to determine side
        if (Math.Abs(normal.X) > Math.Abs(normal.Y))
        {
            return normal.X > 0 ? RelativeReferential.Left : RelativeReferential.Right;
        }
        if (Math.Abs(normal.Y) > Math.Abs(normal.X))
        {
            return normal.Y > 0 ? RelativeReferential.Top : RelativeReferential.Bottom;
        }
        return null; // Diagonal — ambiguous
    }
}
