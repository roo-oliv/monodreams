using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace HeartfeltLending.Entities;

public static class Background
{
    public static Entity Create(World world, Texture2D image)
    {
        var entity = world.CreateEntity();
        entity.Set(new Position(new Vector2(-image.Width/ 2, -image.Height / 2)));
        entity.Set(new DrawInfo(image, new Color(255, 255, 255, 160)));
        return entity;
    }
}