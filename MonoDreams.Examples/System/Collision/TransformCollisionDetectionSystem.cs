using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System.Collision;

namespace MonoDreams.Examples.System.Collision;

/// <summary>
/// Collision detection system that works with Transform component instead of Position.
/// Uses float-precision CollisionRect to avoid integer truncation artifacts.
/// </summary>
public class TransformCollisionDetectionSystem<TCollisionMessage>(
    World world, IParallelRunner parallelRunner, CreateCollisionMessageDelegate<TCollisionMessage> createCollisionMessage)
    : AEntitySetSystem<GameState>(
        world.GetEntities()
            .With((in BoxCollider c) => !c.Passive && c.Enabled)
            .With<Transform>()
            .AsSet(),
        parallelRunner)
    where TCollisionMessage : ICollisionMessage
{
    private readonly IEnumerable<Entity> _targets = world.GetEntities().With((in BoxCollider c) => c.Enabled).AsEnumerable();

    protected override void Update(GameState state, in Entity entity)
    {
        var collidable = entity.Get<BoxCollider>();
        var transform = entity.Get<Transform>();
        var dynamicRect = CollisionRect.FromBounds(collidable.Bounds, transform.Position);
        var displacement = transform.Delta;

        foreach (var target in _targets)
        {
            if (target == entity) continue;

            var targetCollidable = target.Get<BoxCollider>();
            ICollider colliderA = collidable;
            ICollider colliderB = targetCollidable;
            if (!colliderB.SharesLayerWith(colliderA)) continue;

            var collides = DynamicRectVsRect(
                dynamicRect,
                displacement,
                CollisionRect.FromBounds(targetCollidable.Bounds, target.Get<Transform>().Position),
                out var contactPoint,
                out var contactNormal,
                out var contactTime);

            if (!collides) continue;
            if (contactNormal == Vector2.Zero) continue;

            foreach (var layer in colliderB.SharedLayers(colliderA))
            {
                world.Publish(createCollisionMessage(entity, target, contactPoint, contactNormal, contactTime, layer));
            }
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
}
