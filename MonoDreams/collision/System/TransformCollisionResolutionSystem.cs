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
/// Collision resolution system supporting both BoxColliderComponent (swept AABB) and ConvexColliderComponent (SAT).
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
        var colliderA = collision.ColliderA;
        var colliderB = collision.ColliderB;
        var bodyA = collision.BodyA;
        var bodyB = collision.BodyB;

        // The correction target is the BODY (pre-mortem #1: correcting a collider CHILD would drift
        // it inside its parent). Geometry comes from the colliders; the write-back lands on BodyA.
        if (!colliderA.IsAlive || !colliderB.IsAlive || !bodyA.IsAlive) return;
        if (!bodyA.Has<TransformComponent>() || !colliderA.Has<TransformComponent>() || !colliderB.Has<TransformComponent>()) return;

        var hasBoxA = colliderA.Has<BoxColliderComponent>();
        var hasBoxB = colliderB.Has<BoxColliderComponent>();

        if (hasBoxA && hasBoxB)
        {
            ResolveBoxVsBox(colliderA, colliderB, bodyA, bodyB);
        }
        else
        {
            ResolveSAT(colliderA, colliderB, bodyA, bodyB, hasBoxA, hasBoxB);
        }
    }

    private void ResolveBoxVsBox(Entity colliderA, Entity colliderB, Entity bodyA, Entity bodyB)
    {
        ref var bodyTransform = ref bodyA.Get<TransformComponent>();
        // Collider world rects (centered on each collider entity's WorldPosition). The write-backs
        // below TRANSLATE the BODY: for a root body world delta == local delta, and the collider
        // child follows via its parent's world matrix. A dynamic body that is itself a child would
        // need a world→local map (out of scope, interim — the same limitation as before CE).
        var dynamicRect = SATCollision.BoxWorldRect(colliderA.Get<BoxColliderComponent>(), colliderA.Get<TransformComponent>());
        var targetRect = SATCollision.BoxWorldRect(colliderB.Get<BoxColliderComponent>(), colliderB.Get<TransformComponent>());

        // ALREADY INTERPENETRATING: push out along the axis of least overlap (the minimum translation
        // vector) instead of solving the sweep. A swept solve whose START is inside the target returns
        // a NEGATIVE contact time, and its "contact point" then sits arbitrarily far back along the
        // motion — proportional to the TARGET's size, so resolving against one big merged terrain
        // rectangle teleports the body across it (and, at a world edge, clean out of the world, where
        // it falls forever). Penetration is not a sweep; it is a depenetration, and this is the
        // shortest correction that ends it. Bodies that merely TOUCH do not intersect (CollisionRect
        // uses strict bounds), so resting contacts still take the sweep path below, unchanged.
        if (dynamicRect.Intersects(targetRect))
        {
            ResolvePenetration(dynamicRect, targetRect, bodyA, ref bodyTransform);
            return;
        }

        if (!TransformCollisionDetectionSystem<TCollisionMessage>.DynamicRectVsRect(dynamicRect, bodyTransform.Delta, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime)) return;

        if (contactNormal.X != 0)
        {
            // World correction that lands the collider's center at the swept contact point.
            bodyTransform.TranslateX(contactPoint.X - dynamicRect.Center.X);

            if (bodyA.Has<VelocityComponent>())
            {
                ref var velocity = ref bodyA.Get<VelocityComponent>();
                velocity.Current.X = 0;
            }

            if ((int)Math.Abs(contactPoint.X + dynamicRect.Width / 2f - targetRect.Left) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(bodyB, RelativeReferential.Left));
            }
            else if ((int)Math.Abs(contactPoint.X - dynamicRect.Width / 2f - targetRect.Right) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(bodyB, RelativeReferential.Right));
            }
        }

        if (contactNormal.Y != 0)
        {
            bodyTransform.TranslateY(contactPoint.Y - dynamicRect.Center.Y);

            if (bodyA.Has<VelocityComponent>())
            {
                ref var velocity = ref bodyA.Get<VelocityComponent>();
                velocity.Current.Y = 0;
            }

            if ((int)Math.Abs(contactPoint.Y + dynamicRect.Height / 2f - targetRect.Top) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(bodyB, RelativeReferential.Top));
            }
            else if ((int)Math.Abs(contactPoint.Y - dynamicRect.Height / 2f - targetRect.Bottom) == 0)
            {
                _world.Publish(new RigidBodyTouchMessage(bodyB, RelativeReferential.Bottom));
            }
        }
    }

    // Reusable buffers for box-to-polygon conversion
    private readonly Vector2[] _boxBufA = new Vector2[4];
    private readonly Vector2[] _boxBufB = new Vector2[4];

    /// <summary>
    /// Depenetrates two overlapping boxes: translate the body along whichever of the four exits is
    /// shortest, and kill the velocity component on that axis. Independent of how deep the overlap is
    /// and of how big the target is, so being stuck inside terrain self-heals in a frame or two
    /// instead of throwing the body somewhere arbitrary.
    /// </summary>
    private void ResolvePenetration(in CollisionRect dynamicRect, in CollisionRect targetRect,
        Entity bodyA, ref TransformComponent bodyTransform)
    {
        var pushLeft = dynamicRect.Right - targetRect.Left;   // move -X by this to clear
        var pushRight = targetRect.Right - dynamicRect.Left;  // move +X
        var pushUp = dynamicRect.Bottom - targetRect.Top;     // move -Y (y grows down)
        var pushDown = targetRect.Bottom - dynamicRect.Top;   // move +Y

        var horizontal = MathF.Min(pushLeft, pushRight);
        var vertical = MathF.Min(pushUp, pushDown);

        if (horizontal <= vertical)
        {
            bodyTransform.TranslateX(pushLeft <= pushRight ? -pushLeft : pushRight);
            if (bodyA.Has<VelocityComponent>())
            {
                ref var velocity = ref bodyA.Get<VelocityComponent>();
                velocity.Current.X = 0;
            }
        }
        else
        {
            bodyTransform.TranslateY(pushUp <= pushDown ? -pushUp : pushDown);
            if (bodyA.Has<VelocityComponent>())
            {
                ref var velocity = ref bodyA.Get<VelocityComponent>();
                velocity.Current.Y = 0;
            }
        }
    }

    private void ResolveSAT(Entity colliderA, Entity colliderB, Entity bodyA, Entity bodyB, bool hasBoxA, bool hasBoxB)
    {
        ref var bodyTransform = ref bodyA.Get<TransformComponent>();
        var colliderTransformA = colliderA.Get<TransformComponent>();

        // Build current polygons from the COLLIDER entities' world transforms (colliderA's world
        // position already reflects any correction applied to its body earlier this frame, via the
        // lazy WorldMatrix getter — so sequential per-message correction re-validates cleanly).
        Vector2[] polyA;
        Vector2[] polyB;

        if (hasBoxA)
        {
            SATCollision.BoxToPolygon(colliderA.Get<BoxColliderComponent>(), colliderTransformA, _boxBufA);
            polyA = _boxBufA;
        }
        else
        {
            var convex = colliderA.Get<ConvexColliderComponent>();
            convex.UpdateWorldVertices(colliderTransformA);
            polyA = convex.WorldVertices;
        }

        if (hasBoxB)
        {
            SATCollision.BoxToPolygon(colliderB.Get<BoxColliderComponent>(), colliderB.Get<TransformComponent>(), _boxBufB);
            polyB = _boxBufB;
        }
        else
        {
            polyB = colliderB.Get<ConvexColliderComponent>().WorldVertices;
        }

        // Re-validate with current positions
        if (!SATCollision.PolygonVsPolygon(polyA, polyB, out var contactNormal, out var penetrationDepth)) return;

        // Push the BODY out by the MTV (world-space; a root body maps 1:1, the collider child follows).
        bodyTransform.Translate(-contactNormal * penetrationDepth);

        // VelocityComponent damping: remove the body's velocity component moving into the collision
        if (bodyA.Has<VelocityComponent>())
        {
            ref var velocity = ref bodyA.Get<VelocityComponent>();
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
            _world.Publish(new RigidBodyTouchMessage(bodyB, side.Value));
        }
    }

    /// <summary>
    /// Maps a contact normal to a <see cref="RelativeReferential"/> side using the predominant axis.
    /// Returns <c>null</c> when X and Y components are exactly equal (45° diagonal), in which case
    /// no <see cref="RigidBodyTouchMessage"/> is published. This could affect grounded-state detection
    /// on exactly 45° slopes.
    /// </summary>
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
