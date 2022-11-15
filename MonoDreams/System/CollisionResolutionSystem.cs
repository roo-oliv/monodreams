using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System
{
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
            if (_collisions.Count == 0)
            {
                return;
            }
            
            _collisions.Sort((l, r) => l.ContactTime.CompareTo(r.ContactTime));
            foreach (var collision in _collisions)
            {
                var entity = collision.BaseEntity;
                ref var dynamicRect = ref entity.Get<DrawInfo>().Destination;
                ref var body = ref entity.Get<DynamicBody>();
                var displacement = body.NextPosition - body.CurrentPosition;
                ref var targetRect = ref collision.CollidingEntity.Get<DrawInfo>().Destination;
                if (!CollisionDetectionSystem.DynamicRectVsRect(dynamicRect, displacement, targetRect,
                    out var contactPoint, out var contactNormal, out var contactTime)) continue;
                if (contactNormal.X != 0)
                {
                    // position.ArtificialIncrement(contactPoint.X - dynamicRect.Width / 2f - position.CurrentValue.X, 0);
                    // position.UpdateValue(new (contactPoint.X - dynamicRect.Width / 2f, position.CurrentValue.Y));
                    body.NextPosition.X = contactPoint.X - dynamicRect.Width / 2f;
                    body.CurrentPosition.X = body.NextPosition.X;
                }
                if (contactNormal.Y != 0)
                {
                    // position.ArtificialIncrement(0, contactPoint.Y - dynamicRect.Height / 2f - position.CurrentValue.Y);
                    // position.UpdateValue(new (position.CurrentValue.X, contactPoint.Y - dynamicRect.Height / 2f));
                    body.NextPosition.Y = contactPoint.Y - dynamicRect.Height / 2f;
                    body.CurrentPosition.Y = body.NextPosition.Y;
                }

                if (contactNormal.Y < 0)
                {
                    body.IsRiding = true;
                }
                //entity.Remove<Collision>();
            }
            _collisions.Clear();
        }
    }
}