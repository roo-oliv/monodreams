using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component.Draw;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Draw;

public static class CullingSystem
{
    public static void Register(World world, MonoDreams.Component.Camera camera)
    {
        world.System<SpriteInfo, Position>()
            .Kind(Ecs.OnUpdate)
            .Each((Entity entity, ref SpriteInfo spriteInfo, ref Position position) =>
            {
                // Calculate entity bounds in world space
                var entityBounds = new Rectangle(
                    (int)(position.Current.X + spriteInfo.Offset.X),
                    (int)(position.Current.Y + spriteInfo.Offset.Y),
                    (int)spriteInfo.Size.X,
                    (int)spriteInfo.Size.Y
                );
                
                // Check if entity is visible
                var isVisible = camera.VirtualScreenBounds.Intersects(entityBounds);
                
                // Add or remove the Visible component based on culling result
                if (isVisible)
                {
                    if (!entity.Has<Visible>())
                    {
                        entity.Set(new Visible());
                    }
                }
                else
                {
                    if (entity.Has<Visible>())
                    {
                        entity.Remove<Visible>();
                    }
                }
            });
    }
}