using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace HeartfeltLending.Entities;

public static class StaticText
{
    public static Entity Create(
        World world, string value, Vector2 position, BitmapFont font, Color? color = null,
        HorizontalAlign horizontalAlign = HorizontalAlign.Center, VerticalAlign verticalAlign = VerticalAlign.Center)
    {
        var entity = world.CreateEntity();
        entity.Set(new Text
        {
            Value = value,
            Font = font,
            Color = color ?? Color.White,
            HorizontalAlign = horizontalAlign,
            VerticalAlign = verticalAlign,
        });
        entity.Set(new Position(position));
        return entity;
    }
}