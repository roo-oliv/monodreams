using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class CollisionDetectionSystem : AEntitySetSystem<GameState>
{
    private readonly IEnumerable<Entity> _targets;
    private readonly World _world;
        
    public CollisionDetectionSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With((in Collidable c) => !c.Passive).AsSet(), runner)
    {
        _targets = world.GetEntities().With<Collidable>().AsEnumerable();
        _world = world;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        var bounds = entity.Get<Collidable>().Bounds;
        var position = entity.Get<Position>();
        var dynamicRect = new Rectangle(bounds.Location + position.CurrentLocation.ToPoint(), bounds.Size);
        var displacement = position.NextLocation - position.CurrentLocation;
        foreach (var target in _targets)
        {
            if (target == entity)
            {
                continue;
            }
            
            var targetBounds = target.Get<Collidable>().Bounds;
            var targetPosition = target.Get<Position>();
            var targetRect = new Rectangle(targetBounds.Location + targetPosition.CurrentLocation.ToPoint(), targetBounds.Size);

            var collides = DynamicRectVsRect(
                dynamicRect, displacement, targetRect,
                out var contactPoint,
                out var contactNormal,
                out var contactTime);
                
            if (!collides)
            {
                continue;
            }
            
            _world.Publish(new CollisionMessage(entity, target, contactPoint, contactNormal, contactTime));
        }
    }
        
    private static bool RayVsRect(
        Vector2 rayOrigin, Vector2 rayDirection, Rectangle target, out Vector2 contactPoint,
        out Vector2 contactNormal, out float closestHit)
    {
        closestHit = float.NaN;
        contactNormal = Vector2.Zero;
        contactPoint = Vector2.Zero;

        // Cache division
        var inverseDirection = Vector2.One / rayDirection;

        // Calculate intersections with rectangle bounding axes
        var closestDistance = (target.Location.ToVector2() - rayOrigin) * inverseDirection;
        var furthestDistance = (target.Location.ToVector2() + target.Size.ToVector2() - rayOrigin) * inverseDirection;

        if (float.IsNaN(furthestDistance.Y) || float.IsNaN(furthestDistance.X)) return false;
        if (float.IsNaN(closestDistance.Y) || float.IsNaN(closestDistance.X)) return false;

        // Sort distances
        if (closestDistance.X > furthestDistance.X) Utils.Swap(ref closestDistance.X, ref furthestDistance.X);
        if (closestDistance.Y > furthestDistance.Y) Utils.Swap(ref closestDistance.Y, ref furthestDistance.Y);

        // Early rejection
        if (closestDistance.X > furthestDistance.Y || closestDistance.Y > furthestDistance.X) return false;

        // Closest 'time' will be the first contact
        closestHit = Math.Max(closestDistance.X, closestDistance.Y);

        // Furthest 'time' is contact on opposite side of target
        var furthestHit = Math.Min(furthestDistance.X, furthestDistance.Y);

        // Reject if ray direction is pointing away from object
        if (furthestHit < 0)
            return false;

        // Contact point of collision from parametric line equation
        contactPoint = rayOrigin + closestHit * rayDirection;

        if (closestDistance.X > closestDistance.Y)
            contactNormal = inverseDirection.X < 0 ? new Vector2(1, 0) : new Vector2(-1, 0);
        else if (closestDistance.X < closestDistance.Y)
            contactNormal = inverseDirection.Y < 0 ? new Vector2(0, 1) : new Vector2(0, -1);

        // Note if t_near == t_far, collision is principally in a diagonal
        // so pointless to resolve. By returning a CN={0,0} even though its
        // considered a hit, the resolver wont change anything.
        return true;
    }
        
    public static bool DynamicRectVsRect(
        in Rectangle dynamicRect, in Vector2 displacement, in Rectangle staticRect,
        out Vector2 contactPoint, out Vector2 contactNormal, out float contactTime)
    {
        // Check if dynamic rectangle is actually moving - if not, we just check for intersection
        if (displacement is { X: 0, Y: 0 })
        {
            contactPoint = Vector2.Zero;
            contactNormal = Vector2.Zero;
            contactTime = 0;
            return dynamicRect.Intersects(staticRect);
        }

        // Expand target rectangle by source dimensions
        var expandedTarget = new Rectangle(
            (staticRect.Location.ToVector2() - dynamicRect.Size.ToVector2() / 2).ToPoint(),
            staticRect.Size + dynamicRect.Size);

        var potentialCollision = RayVsRect(
            dynamicRect.Center.ToVector2(), displacement, expandedTarget, out contactPoint,
            out contactNormal, out contactTime);

        // return potentialCollision && contactTime is >= 0.0f and < 1.0f;
        return potentialCollision && contactTime < 1.0f;
    }
}