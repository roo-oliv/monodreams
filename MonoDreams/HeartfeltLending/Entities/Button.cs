using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace HeartfeltLending.Entities;

public static class Button
{
    public static Entity Create(
        World world, string value, Vector2 position, Point size, BitmapFont font, Color? defaultColor = null, Color? selectedColor = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new Position(position));
        entity.Set(new Collidable(new Rectangle(size/new Point(-2, -2), size)));
        entity.Set(new Text
        {
            Value = value,
            Font = font,
            Color = defaultColor ?? Color.White,
            HorizontalAlign = HorizontalAlign.Center,
            VerticalAlign = VerticalAlign.Center,
        });
        entity.Set(new ButtonState(defaultColor ?? Color.White, selectedColor ?? Color.Gray));
        return entity;
    }
}