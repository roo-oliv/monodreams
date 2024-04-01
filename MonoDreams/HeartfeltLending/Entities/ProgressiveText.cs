using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace HeartfeltLending.Entities;

public class ProgressiveText
{
    public static Entity Create(World world, string value, Vector2 position, BitmapFont font)
    {
        var entity = world.CreateEntity();
        entity.Set(new DynamicText(value, font));
        entity.Set(new Position(position));
        return entity;
    }
}