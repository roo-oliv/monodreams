using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class CollisionSystem : AEntitySetSystem<GameState>
    {
        private readonly IEnumerable<Entity> targets;
        
        public CollisionSystem(World world)
            : base(world.GetEntities().With<DynamicBody>().With<DrawInfo>().AsSet(), true)
        {
            targets = world.GetEntities().With<Solid>().With<DrawInfo>().AsEnumerable();
        }

        protected override void Update(GameState state, ReadOnlySpan<Entity> entities)
        {
            foreach (var entity in entities)
            {
                ref var dynamicRect = ref entity.Get<DrawInfo>().Destination;
                var displacement = entity.Get<Velocity>().Value * state.Time;
                foreach (var target in targets)
                {
                    ref var targetRect = ref target.Get<DrawInfo>().Destination;
                    Vector2 contactPoint;
                    Vector2 contactNormal;
                    float contactTime;
                    if (DynamicRectVsRect(dynamicRect, displacement, targetRect, out contactPoint, out contactNormal,
                        out contactTime))
                    {
                        // Console.WriteLine("Coliiisiooon");
                        // Console.WriteLine(contactPoint);
                        // Console.WriteLine(contactNormal);
                        // Console.WriteLine(contactTime);
                        World.CreateEntity().Set(new DrawInfo
                        {
                            Color = Color.Green,
                            Destination = new Rectangle(contactPoint.ToPoint(), new Point(10, 10))
                        });
                        World.CreateEntity().Set(new DrawInfo
                        {
                            Color = Color.Yellow,
                            Destination = new Rectangle(contactPoint.ToPoint(), (contactNormal + new Vector2(5, 100)).ToPoint())
                        });
                        
                    }
                    
                } 
            // for (var i = 0; i < entities.Length; ++i)
            // {
            //     ref readonly var iEntity = ref entities[i];
            //     var iBound = iEntity.Get<DrawInfo>().Destination;
            //     for (var j = i + 1; j < entities.Length; ++j)
            //     {
            //         ref readonly var jEntity = ref entities[j];
            //         var jBound = jEntity.Get<DrawInfo>().Destination;
            //         if (!iBound.Intersects(jBound))
            //         {
            //             continue;
            //         }
            //         
            //         iEntity.Set(new Collision(in jEntity));
            //         jEntity.Set(new Collision(in iEntity));
            //     }
                // ---------------------------------------------
                // Rectangle ballXBound = new((int)position.Value.X, (int)(position.Value.Y - (velocity.Value.Y * state.Time)), 10, 10);
                // Rectangle ballYBound = new((int)(position.Value.X - (velocity.Value.X * state.Time)), (int)position.Value.Y, 10, 10);
                //
                // int speedUp = 0;
                //
                // foreach (ref readonly Entity solid in entities)
                // {
                //     Rectangle bound = solid.Get<DrawInfo>().Destination;
                //
                //     if (ballXBound.Intersects(bound))
                //     {
                //         if (ballYBound.X - (velocity.Value.X * state.Time) < bound.X + bound.Width)
                //         {
                //             position.Value.X -= (position.Value.X + 10 - bound.X) * 2;
                //         }
                //         else
                //         {
                //             position.Value.X -= (position.Value.X - bound.X - bound.Width) * 2;
                //         }
                //
                //         velocity.Value.X *= -1;
                //     }
                //     else if (ballYBound.Intersects(bound))
                //     {
                //         if (ballXBound.Y - (velocity.Value.Y * state.Time) < bound.Y + bound.Height)
                //         {
                //             position.Value.Y -= (position.Value.Y + 10 - bound.Y) * 2;
                //         }
                //         else
                //         {
                //             position.Value.Y -= (position.Value.Y - bound.Y - bound.Height) * 2;
                //         }
                //
                //         velocity.Value.Y *= -1;
                //     }
                // }
                //
                // ball.Set(position);
                // velocity.Value += Vector2.Normalize(velocity.Value) * speedUp * 10f;
            }
        }
        
        private static bool RayVsRect(
            in Vector2 rayOrigin, in Vector2 rayDirection, in Rectangle target, out Vector2 contactPoint,
            out Vector2 contactNormal, out float nearestHitDistance)
        {
            nearestHitDistance = float.NaN;
            contactNormal = new Vector2(0,0);
            contactPoint = new Vector2(0,0);

            // Cache division
            var inverseDirection = rayDirection * 0.5f;

            // Calculate intersections with rectangle bounding axes
            var nearestDistance = (target.Center.ToVector2() - rayOrigin) * inverseDirection;
            var farthestDistance = (target.Center.ToVector2() + target.Size.ToVector2() - rayOrigin) * inverseDirection;

            if (float.IsNaN(farthestDistance.Y) || float.IsNaN(farthestDistance.X)) return false;
            if (float.IsNaN(nearestDistance.Y) || float.IsNaN(nearestDistance.X)) return false;

            // Sort distances
            if (nearestDistance.X > farthestDistance.X) Utils.Swap(ref nearestDistance.X, ref farthestDistance.X);
            if (nearestDistance.Y > farthestDistance.Y) Utils.Swap(ref nearestDistance.Y, ref farthestDistance.Y);

            // Early rejection
            if (nearestDistance.X > farthestDistance.Y || nearestDistance.Y > farthestDistance.X) return false;

            // Closest 'time' will be the first contact
            nearestHitDistance = Math.Max(nearestDistance.X, nearestDistance.Y);

            // Furthest 'time' is contact on opposite side of target
            var farthestHitDistance = Math.Min(farthestDistance.X, farthestDistance.Y);

            // Reject if ray direction is pointing away from object
            if (farthestHitDistance < 0)
                return false;

            // Contact point of collision from parametric line equation
            contactPoint = rayOrigin + nearestHitDistance * rayDirection;

            if (nearestDistance.X > nearestDistance.Y)
                contactNormal = inverseDirection.X < 0 ? new Vector2(1, 0) : new Vector2(-1, 0);
            else if (nearestDistance.X < nearestDistance.Y)
                contactNormal = inverseDirection.Y < 0 ? new Vector2(0, 1) : new Vector2(0, -1);

            // Note if t_near == t_far, collision is principly in a diagonal
            // so pointless to resolve. By returning a CN={0,0} even though its
            // considered a hit, the resolver wont change anything.
            return true;
        }
        
        private static bool DynamicRectVsRect(
            in Rectangle dynamicRect, in Vector2 displacement, in Rectangle staticRect,
            out Vector2 contactPoint, out Vector2 contactNormal, out float contactTime)
        {
            // Check if dynamic rectangle is actually moving - we assume rectangles are NOT in collision to start
            if (displacement.X == 0 && displacement.Y == 0)
            {
                contactPoint = default;
                contactNormal = default;
                contactTime = 0;
                return false;
            }

            // Expand target rectangle by source dimensions
            var expandedTarget = new Rectangle(
                (staticRect.Location.ToVector2() - dynamicRect.Size.ToVector2() / 2).ToPoint(),
                staticRect.Size + dynamicRect.Size);

            if (
                RayVsRect(
                    dynamicRect.Center.ToVector2(), in displacement, in expandedTarget, out contactPoint, 
                    out contactNormal, out contactTime)
            )
                return (contactTime is >= 0.0f and < 1.0f);
            return false;
        }
    }
}