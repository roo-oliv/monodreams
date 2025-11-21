using System;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Collision;
using MonoDreams.Examples.Message;
using MonoDreams.Extensions.Monogame;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Collision;

/// <summary>
/// Resolves physical collisions by separating overlapping entities and zeroing velocity.
/// Reads collision data from singleton CollisionBuffer, processes only Physics-type collisions,
/// sorts by contact time for stable resolution, and clears the buffer after processing.
/// </summary>
public static class PhysicalCollisionResolutionSystem
{
    public static void Register(World world)
    {
        world.System("PhysicalCollisionResolutionSystem")
            .Kind(PhysicsPhase)
            .Iter((Iter it) =>
            {
                // Clear all collision states first
                world.Each((Entity entity, ref CollisionState state) =>
                {
                    state.IsColliding = false;
                    state.CollisionCount = 0;
                });

                // Get the collision buffer
                ref var collisionBuffer = ref world.GetMut<CollisionBuffer>();

                // Filter for physics collisions only
                var physicsCollisions = collisionBuffer.Collisions
                    .Where(c => c.Type == CollisionType.Physics)
                    .OrderBy(c => c.ContactTime)
                    .ToList();

                if (physicsCollisions.Count > 0)
                {
                    Console.WriteLine($"[Collision] Resolving {physicsCollisions.Count} collision(s)");
                }

                // Process each collision in order of contact time (earliest first)
                foreach (var collision in physicsCollisions)
                {
                    ResolveCollision(world, collision);
                }

                // Clear the buffer for next frame
                collisionBuffer.Collisions.Clear();
            });
    }

    private static void ResolveCollision(World world, CollisionMessage collision)
    {
        // Get Flecs entities from stored IDs
        var entity = world.Entity(collision.BaseEntityId);
        var collidingEntity = world.Entity(collision.CollidingEntityId);

        // Check entities still exist and have required components
        if (!entity.IsAlive() || !collidingEntity.IsAlive()) return;
        if (!entity.Has<Position>() || !entity.Has<BoxCollider>()) return;
        if (!collidingEntity.Has<Position>() || !collidingEntity.Has<BoxCollider>()) return;

        ref var position = ref entity.GetMut<Position>();
        ref var collider = ref entity.GetMut<BoxCollider>();
        var dynamicRect = collider.Bounds.AtPosition(position.Current);

        ref var targetPosition = ref collidingEntity.GetMut<Position>();
        ref var targetCollider = ref collidingEntity.GetMut<BoxCollider>();
        var targetRect = targetCollider.Bounds.AtPosition(targetPosition.Current);

        // Re-check collision (positions may have changed from earlier resolutions in this frame)
        if (!CollisionDetectionSystem.DynamicRectVsRect(
                dynamicRect, position.Delta, targetRect,
                out var contactPoint, out var contactNormal, out var contactTime))
            return;

        Console.WriteLine($"[Collision] Entity {collision.BaseEntityId} colliding with {collision.CollidingEntityId} - Normal: {contactNormal}, Time: {contactTime}");

        // Mark entities as colliding (add CollisionState if not present)
        if (!entity.Has<CollisionState>())
            entity.Set(new CollisionState());
        if (!collidingEntity.Has<CollisionState>())
            collidingEntity.Set(new CollisionState());

        ref var entityCollisionState = ref entity.GetMut<CollisionState>();
        ref var targetCollisionState = ref collidingEntity.GetMut<CollisionState>();

        entityCollisionState.IsColliding = true;
        targetCollisionState.IsColliding = true;
        entityCollisionState.CollisionCount++;
        targetCollisionState.CollisionCount++;

        // Resolve X-axis collision
        if (contactNormal.X != 0)
        {
            position.Current.X = contactPoint.X - dynamicRect.Width / 2f - collider.Bounds.Location.X;

            // Zero X velocity if entity has Velocity component
            if (entity.Has<Velocity>())
            {
                ref var velocity = ref entity.GetMut<Velocity>();
                velocity.Current.X = 0;
            }

            // Note: RigidBodyTouchMessage publishing skipped for now (can add later if needed)
        }

        // Resolve Y-axis collision
        if (contactNormal.Y != 0)
        {
            position.Current.Y = contactPoint.Y - dynamicRect.Height / 2f - collider.Bounds.Location.Y;

            // Zero Y velocity if entity has Velocity component
            if (entity.Has<Velocity>())
            {
                ref var velocity = ref entity.GetMut<Velocity>();
                velocity.Current.Y = 0;
            }

            // Note: RigidBodyTouchMessage publishing skipped for now (can add later if needed)
        }
    }
}
