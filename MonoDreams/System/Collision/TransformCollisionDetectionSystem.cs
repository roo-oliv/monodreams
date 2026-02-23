using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System.Collision;

/// <summary>
/// Collision detection system supporting both BoxCollider (swept AABB) and ConvexCollider (SAT).
/// Uses ColliderTag marker component to query entities with either collider type.
/// </summary>
public class TransformCollisionDetectionSystem<TCollisionMessage> : ISystem<GameState>
    where TCollisionMessage : ICollisionMessage
{
    private readonly World _world;
    private readonly EntitySet _activeSet;
    private readonly IEnumerable<Entity> _targets;
    private readonly CreateCollisionMessageDelegate<TCollisionMessage> _createCollisionMessage;

    public bool IsEnabled { get; set; } = true;

    public TransformCollisionDetectionSystem(
        World world,
        IParallelRunner parallelRunner,
        CreateCollisionMessageDelegate<TCollisionMessage> createCollisionMessage)
    {
        _world = world;
        _createCollisionMessage = createCollisionMessage;

        // Auto-tag entities when they get a collider component
        world.SubscribeEntityComponentAdded<BoxCollider>(OnBoxColliderAdded);
        world.SubscribeEntityComponentAdded<ConvexCollider>(OnConvexColliderAdded);

        // Active (non-passive, enabled) entities that can initiate collisions
        _activeSet = world.GetEntities()
            .With<ColliderTag>()
            .With<Transform>()
            .AsSet();

        // All enabled collider entities are potential targets
        _targets = world.GetEntities()
            .With<ColliderTag>()
            .With<Transform>()
            .AsEnumerable();
    }

    private static void OnBoxColliderAdded(in Entity entity, in BoxCollider _)
    {
        if (!entity.Has<ColliderTag>()) entity.Set<ColliderTag>();
    }

    private static void OnConvexColliderAdded(in Entity entity, in ConvexCollider _)
    {
        if (!entity.Has<ColliderTag>()) entity.Set<ColliderTag>();
    }

    public void Update(GameState state)
    {
        // First pass: update world vertices for all ConvexCollider entities
        foreach (var entity in _activeSet.GetEntities())
        {
            if (entity.Has<ConvexCollider>())
            {
                ref var convex = ref entity.Get<ConvexCollider>();
                convex.UpdateWorldVertices(entity.Get<Transform>());
            }
        }

        foreach (var target in _targets)
        {
            if (target.Has<ConvexCollider>())
            {
                ref var convex = ref target.Get<ConvexCollider>();
                convex.UpdateWorldVertices(target.Get<Transform>());
            }
        }

        // Second pass: test collisions
        foreach (var entity in _activeSet.GetEntities())
        {
            var colliderA = GetCollider(entity);
            if (colliderA == null || colliderA.Passive || !colliderA.Enabled) continue;

            foreach (var target in _targets)
            {
                if (target == entity) continue;

                var colliderB = GetCollider(target);
                if (colliderB == null || !colliderB.Enabled) continue;
                if (!colliderB.SharesLayerWith(colliderA)) continue;

                var hasBoxA = entity.Has<BoxCollider>();
                var hasBoxB = target.Has<BoxCollider>();

                if (hasBoxA && hasBoxB)
                {
                    // Box-vs-Box: existing swept AABB path (unchanged)
                    TestBoxVsBox(entity, target, colliderA, colliderB);
                }
                else
                {
                    // Any polygon pair: SAT
                    TestSAT(entity, target, colliderA, colliderB, hasBoxA, hasBoxB);
                }
            }
        }
    }

    private void TestBoxVsBox(Entity entity, Entity target, ICollider colliderA, ICollider colliderB)
    {
        var boxA = entity.Get<BoxCollider>();
        var transformA = entity.Get<Transform>();
        var dynamicRect = CollisionRect.FromBounds(boxA.Bounds, transformA.Position);
        var displacement = transformA.Delta;

        var boxB = target.Get<BoxCollider>();
        var transformB = target.Get<Transform>();
        var targetRect = CollisionRect.FromBounds(boxB.Bounds, transformB.Position);

        var collides = DynamicRectVsRect(
            dynamicRect, displacement, targetRect,
            out var contactPoint, out var contactNormal, out var contactTime);

        if (!collides || contactNormal == Vector2.Zero) return;

        foreach (var layer in colliderB.SharedLayers(colliderA))
        {
            _world.Publish(_createCollisionMessage(entity, target, contactPoint, contactNormal, contactTime, 0f, layer));
        }
    }

    // Reusable buffers for box-to-polygon conversion (avoids stackalloc scope issues)
    private readonly Vector2[] _boxPolyBufA = new Vector2[4];
    private readonly Vector2[] _boxPolyBufB = new Vector2[4];

    private void TestSAT(Entity entity, Entity target, ICollider colliderA, ICollider colliderB, bool hasBoxA, bool hasBoxB)
    {
        // Broad-phase AABB rejection first (cheap)
        var aabbA = hasBoxA
            ? CollisionRect.FromBounds(entity.Get<BoxCollider>().Bounds, entity.Get<Transform>().Position)
            : entity.Get<ConvexCollider>().BroadPhaseAABB;
        var aabbB = hasBoxB
            ? CollisionRect.FromBounds(target.Get<BoxCollider>().Bounds, target.Get<Transform>().Position)
            : target.Get<ConvexCollider>().BroadPhaseAABB;

        if (!aabbA.Intersects(aabbB)) return;

        // Get world-space polygons for both entities
        Vector2[] polyA;
        Vector2[] polyB;

        if (hasBoxA)
        {
            SATCollision.BoxToPolygon(entity.Get<BoxCollider>(), entity.Get<Transform>(), _boxPolyBufA);
            polyA = _boxPolyBufA;
        }
        else
        {
            polyA = entity.Get<ConvexCollider>().WorldVertices;
        }

        if (hasBoxB)
        {
            SATCollision.BoxToPolygon(target.Get<BoxCollider>(), target.Get<Transform>(), _boxPolyBufB);
            polyB = _boxPolyBufB;
        }
        else
        {
            polyB = target.Get<ConvexCollider>().WorldVertices;
        }

        if (!SATCollision.PolygonVsPolygon(polyA, polyB, out var contactNormal, out var penetrationDepth)) return;

        // Contact point: midpoint of centroids
        var contactPoint = (SATCollision.PolygonCenter(polyA) + SATCollision.PolygonCenter(polyB)) / 2f;

        foreach (var layer in colliderB.SharedLayers(colliderA))
        {
            _world.Publish(_createCollisionMessage(entity, target, contactPoint, contactNormal, 0f, penetrationDepth, layer));
        }
    }

    private static bool RayVsRect(
        Vector2 rayOrigin, Vector2 rayDirection, CollisionRect target, out Vector2 contactPoint,
        out Vector2 contactNormal, out float closestHit)
    {
        closestHit = float.NaN;
        contactNormal = Vector2.Zero;
        contactPoint = Vector2.Zero;

        var inverseDirection = Vector2.One / rayDirection;
        var closestDistance = (target.Position - rayOrigin) * inverseDirection;
        var furthestDistance = (target.Position + target.Size - rayOrigin) * inverseDirection;

        if (float.IsNaN(furthestDistance.Y) || float.IsNaN(furthestDistance.X)) return false;
        if (float.IsNaN(closestDistance.Y) || float.IsNaN(closestDistance.X)) return false;

        if (closestDistance.X > furthestDistance.X) (closestDistance.X, furthestDistance.X) = (furthestDistance.X, closestDistance.X);
        if (closestDistance.Y > furthestDistance.Y) (closestDistance.Y, furthestDistance.Y) = (furthestDistance.Y, closestDistance.Y);

        if (closestDistance.X > furthestDistance.Y || closestDistance.Y > furthestDistance.X) return false;

        closestHit = Math.Max(closestDistance.X, closestDistance.Y);
        var furthestHit = Math.Min(furthestDistance.X, furthestDistance.Y);

        if (furthestHit < 0) return false;

        contactPoint = rayOrigin + closestHit * rayDirection;

        if (closestDistance.X > closestDistance.Y)
            contactNormal = inverseDirection.X < 0 ? new Vector2(1, 0) : new Vector2(-1, 0);
        else if (closestDistance.X < closestDistance.Y)
            contactNormal = inverseDirection.Y < 0 ? new Vector2(0, 1) : new Vector2(0, -1);

        return true;
    }

    public static bool DynamicRectVsRect(
        in CollisionRect dynamicRect, in Vector2 displacement, in CollisionRect staticRect,
        out Vector2 contactPoint, out Vector2 contactNormal, out float contactTime)
    {
        if (displacement is { X: 0, Y: 0 })
        {
            contactPoint = Vector2.Zero;
            contactNormal = Vector2.Zero;
            contactTime = 0;
            return dynamicRect.Intersects(staticRect);
        }

        var expandedTarget = new CollisionRect(
            staticRect.Position - dynamicRect.Size / 2,
            staticRect.Size + dynamicRect.Size);

        var potentialCollision = RayVsRect(
            dynamicRect.Center, displacement, expandedTarget, out contactPoint,
            out contactNormal, out contactTime);

        return potentialCollision && contactTime < 1.0f;
    }

    private static ICollider GetCollider(Entity entity)
    {
        if (entity.Has<BoxCollider>()) return entity.Get<BoxCollider>();
        if (entity.Has<ConvexCollider>()) return entity.Get<ConvexCollider>();
        return null;
    }

    public void Dispose()
    {
        _activeSet?.Dispose();
    }
}
