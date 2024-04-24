using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Objects;

public static class StaticImage
{
    public static Entity Create(World world, Vector2 location, Texture2D texture, Point? size = null, Rectangle? source = null, Color? color = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new Position(location));
        entity.Set(new DrawInfo(texture, size, source, color));
        return entity;
    }
}