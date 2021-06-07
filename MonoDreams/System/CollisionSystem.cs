using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class CollisionSystem : AEntitySetSystem<GameState>
    {

        public CollisionSystem(World world)
            : base(world.GetEntities().With<Solid>().With<DrawInfo>().AsSet(), true)
        {
        }

        protected override void Update(GameState state, ReadOnlySpan<Entity> entities)
        {
            for (var i = 0; i < entities.Length; ++i)
            {
                ref readonly var iEntity = ref entities[i];
                var iBound = iEntity.Get<DrawInfo>().Destination;
                for (var j = i + 1; j < entities.Length; ++j)
                {
                    ref readonly var jEntity = ref entities[j];
                    var jBound = jEntity.Get<DrawInfo>().Destination;
                    if (!iBound.Intersects(jBound))
                    {
                        continue;
                    }
                    
                    iEntity.Set(new Collision(in jEntity));
                    jEntity.Set(new Collision(in iEntity));
                }
                
                Rectangle ballXBound = new((int)position.Value.X, (int)(position.Value.Y - (velocity.Value.Y * state.Time)), 10, 10);
                Rectangle ballYBound = new((int)(position.Value.X - (velocity.Value.X * state.Time)), (int)position.Value.Y, 10, 10);

                int speedUp = 0;

                foreach (ref readonly Entity solid in entities)
                {
                    Rectangle bound = solid.Get<DrawInfo>().Destination;

                    if (ballXBound.Intersects(bound))
                    {
                        if (ballYBound.X - (velocity.Value.X * state.Time) < bound.X + bound.Width)
                        {
                            position.Value.X -= (position.Value.X + 10 - bound.X) * 2;
                        }
                        else
                        {
                            position.Value.X -= (position.Value.X - bound.X - bound.Width) * 2;
                        }

                        velocity.Value.X *= -1;
                    }
                    else if (ballYBound.Intersects(bound))
                    {
                        if (ballXBound.Y - (velocity.Value.Y * state.Time) < bound.Y + bound.Height)
                        {
                            position.Value.Y -= (position.Value.Y + 10 - bound.Y) * 2;
                        }
                        else
                        {
                            position.Value.Y -= (position.Value.Y - bound.Y - bound.Height) * 2;
                        }

                        velocity.Value.Y *= -1;
                    }
                }

                ball.Set(position);
                velocity.Value += Vector2.Normalize(velocity.Value) * speedUp * 10f;
            }
        }
    }
}