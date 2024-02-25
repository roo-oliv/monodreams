using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace HeartfeltLending.Entities;

public static class Button
{
    public static Entity Create(
        World world, string value, Vector2 position, BitmapFont font, Color? color = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new Position(position));
        entity.Set(new Text
        {
            Value = value,
            Font = font,
            Color = color ?? Color.White,
            HorizontalAlign = HorizontalAlign.Center,
            VerticalAlign = VerticalAlign.Center,
        });
        entity.Set(new DrawInfo());
        return entity;
    }
}