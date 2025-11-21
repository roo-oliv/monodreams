using System;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Message;
using MonoDreams.Extensions.Monogame;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Collision;

/// <summary>
/// Detects collisions between active (non-passive, enabled) colliders and all enabled target colliders.
/// Uses swept AABB (continuous collision detection) to find contact point, normal, and time.
/// Writes collision data to singleton CollisionBuffer for processing by resolution systems.
/// </summary>
public static class CollisionDetectionSystem
{
    public static void Register(World world)
    {
        world.System<BoxCollider, Position>("CollisionDetectionSystem")
            .Kind(PhysicsPhase)
            .Each((Entity activeEntity, ref BoxCollider activeCollider, ref Position activePosition) =>
            {
                // Only check collisions for active (non-passive, enabled) colliders
                if (activeCollider.Passive || !activeCollider.Enabled) return;

                var dynamicRect = activeCollider.Bounds.AtPosition(activePosition.Current);
                var displacement = activePosition.Delta;

                // Debug: log if entity is moving
                if (displacement != Vector2.Zero)
                {
                    Console.WriteLine($"[CollisionDetection] Active entity moving with delta: {displacement}");
                }

                // Get the collision buffer to write results
                ref var collisionBuffer = ref world.GetMut<CollisionBuffer>();
                var activeColliderLayers = activeCollider.ActiveLayers;
                var collisions = collisionBuffer.Collisions;

                // Iterate through all enabled target colliders using world.Each
                world.Each((Entity targetEntity, ref BoxCollider targetCollider, ref Position targetPosition) =>
                {
                    // Skip self-collision
                    if (activeEntity == targetEntity) return;

                    // Only check enabled colliders
                    if (!targetCollider.Enabled) return;

                    // Check layer compatibility
                    if (!targetCollider.ActiveLayers.Overlaps(activeColliderLayers)) return;

                    // Perform swept AABB collision detection
                    var collides = DynamicRectVsRect(
                        dynamicRect,
                        displacement,
                        targetCollider.Bounds.AtPosition(targetPosition.Current),
                        out var contactPoint,
                        out var contactNormal,
                        out var contactTime);

                    if (!collides) return;

                    Console.WriteLine($"[CollisionDetection] Collision detected! Contact normal: {contactNormal}, time: {contactTime}");

                    // Create and add collision messages for each shared layer
                    foreach (var layer in targetCollider.ActiveLayers.Intersect(activeColliderLayers))
                    {
                        var message = CollisionMessage.Create(
                            activeEntity,
                            targetEntity,
                            contactPoint,
                            contactNormal,
                            contactTime,
                            layer);

                        collisions.Add(message);
                    }
                });
            });
    }

    /// <summary>
    /// Performs swept AABB collision detection between a moving rectangle and a static rectangle.
    /// Uses ray-casting against an expanded target rectangle to find collision time and normal.
    /// </summary>
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

        // Expand target rectangle by source dimensions (Minkowski sum)
        var expandedTarget = new Rectangle(
            (staticRect.Location.ToVector2() - dynamicRect.Size.ToVector2() / 2).ToPoint(),
            staticRect.Size + dynamicRect.Size);

        var potentialCollision = RayVsRect(
            dynamicRect.Center.ToVector2(), displacement, expandedTarget, out contactPoint,
            out contactNormal, out contactTime);

        // Only consider collisions that occur during this frame (contactTime < 1.0)
        return potentialCollision && contactTime < 1.0f;
    }

    /// <summary>
    /// Performs ray-vs-rectangle intersection test using inverse direction optimization.
    /// Returns contact point, normal, and time (0.0 to 1.0) along the ray.
    /// </summary>
    private static bool RayVsRect(
        Vector2 rayOrigin, Vector2 rayDirection, Rectangle target, out Vector2 contactPoint,
        out Vector2 contactNormal, out float closestHit)
    {
        closestHit = float.NaN;
        contactNormal = Vector2.Zero;
        contactPoint = Vector2.Zero;

        // Cache division (inverse direction for faster calculation)
        var inverseDirection = Vector2.One / rayDirection;

        // Calculate intersections with rectangle bounding axes
        var closestDistance = (target.Location.ToVector2() - rayOrigin) * inverseDirection;
        var furthestDistance = (target.Location.ToVector2() + target.Size.ToVector2() - rayOrigin) * inverseDirection;

        if (float.IsNaN(furthestDistance.Y) || float.IsNaN(furthestDistance.X)) return false;
        if (float.IsNaN(closestDistance.Y) || float.IsNaN(closestDistance.X)) return false;

        // Sort distances (ensure closestDistance is actually closest)
        if (closestDistance.X > furthestDistance.X) Utils.Swap(ref closestDistance.X, ref furthestDistance.X);
        if (closestDistance.Y > furthestDistance.Y) Utils.Swap(ref closestDistance.Y, ref furthestDistance.Y);

        // Early rejection - no intersection
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

        // Determine contact normal based on which axis hit first
        if (closestDistance.X > closestDistance.Y)
            contactNormal = inverseDirection.X < 0 ? new Vector2(1, 0) : new Vector2(-1, 0);
        else if (closestDistance.X < closestDistance.Y)
            contactNormal = inverseDirection.Y < 0 ? new Vector2(0, 1) : new Vector2(0, -1);

        // Note: if closestDistance.X == closestDistance.Y, collision is diagonal
        // Return contactNormal={0,0} to indicate unresolvable diagonal collision
        return true;
    }
}
