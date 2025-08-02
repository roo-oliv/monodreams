using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;

namespace MonoDreams.Examples.Objects;

public static class Car
{
    public static Entity Create(
        World world, Texture2D carTexture)
    {
        var entity = world.CreateEntity();

        var origin = new Vector2(32);
        
        entity.Set(new EntityInfo(EntityType.Car));
        entity.Set(new Transform(Vector2.Zero, 0f, origin));
        entity.Set(new CarComponent());

        // entity.Set(new DrawComponent
        // {
        //     Type = DrawElementType.Sprite,
        //     Target = RenderTargetID.Main,
        //     Texture = carTexture,
        //     Color = Color.White,
        //     Position = Vector2.Zero,
        //     Size = new Vector2(12, 12),
        //     LayerDepth = 0.2f,
        // });
        // Add sprite information for rendering
        entity.Set(new SpriteInfo
        {
            SpriteSheet = carTexture,
            Source = carTexture.Bounds,
            Size = new Vector2(10, 10),
            Target = RenderTargetID.Main,
            LayerDepth = 1f,
            // Offset = new Vector2(-6),
        });
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });
        entity.Set(new Visible());

        return entity;
    }
}