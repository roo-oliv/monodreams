using System;
using System.Collections;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public class DynamicBodyCollisionResolutionSystem : AEntitySetSystem<GameState>
    {
        private static int CompareCollidingEntities(Entity x, Entity y)
        {
            return x.Get<Collision>().ContactTime.CompareTo(y.Get<Collision>().ContactTime);
        }
        
        public DynamicBodyCollisionResolutionSystem(World world)
            : base(world.GetEntities().With<Collision>().With<DynamicBody>().AsSet(), true)
        {
        }

        protected override void Update(GameState state, ReadOnlySpan<Entity> entities)
        {
            var sortedEntities = entities.ToArray();
            Array.Sort(sortedEntities, CompareCollidingEntities);

            foreach (var entity in sortedEntities)
            {
                ref var velocity = ref entity.Get<Velocity>();
                ref var dynamicRect = ref entity.Get<DrawInfo>().Destination;
                var displacement = entity.Get<Velocity>().Value * state.Time;
                ref var collision = ref entity.Get<Collision>();
                ref var targetRect = ref collision.CollidingEntity.Get<DrawInfo>().Destination;
                if (CollisionDetectionSystem.DynamicRectVsRect(dynamicRect, displacement, targetRect,
                    out var contactPoint, out var contactNormal, out var contactTime))
                {
                    var absVelocity = new Vector2(Math.Abs(velocity.Value.X), Math.Abs(velocity.Value.Y));
                    velocity.Value += contactNormal * absVelocity * (1 - contactTime);
                    ref var dynamicBody = ref entity.Get<DynamicBody>();
                    if (contactNormal.Y > 0)
                    {
                        dynamicBody.IsRiding = true;
                    }
                }
                entity.Remove<Collision>();
            }
        }
    }
}