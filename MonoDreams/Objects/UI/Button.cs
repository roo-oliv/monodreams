using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoGame.Extended;
using MonoGame.Extended.BitmapFonts;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.Objects.UI;

public class Button
{
    public static Entity Create(
        World world, string value, Action callback, Vector2 position, Point size, BitmapFont font,
        Color? defaultColor = null, Color? selectedColor = null, Enum drawLayer = null)
    {
        defaultColor ??= Color.White;
        selectedColor ??= Color.Black;
        var entity = world.CreateEntity();
        entity.Set(new Position(position));
        entity.Set(new BoxCollider(new Rectangle(size/new Point(-2, -2), size)));
        entity.Set(new SimpleText(value, font, defaultColor.Value, HorizontalAlign.Center, VerticalAlign.Center, drawLayer));
        entity.Set(new ButtonState(defaultColor.Value, selectedColor.Value, callback));
        return entity;
    }
    
    public static Entity Create(
        World world, string value, Action callback, Vector2 position, BitmapFont font, Color? defaultColor = null,
        Color? selectedColor = null, Enum drawLayer = null)
    {
        var size = font.MeasureString(value) + new SizeF(5, 5);
        return Create(
            world, value, callback, position, new Point((int)size.Width, (int)size.Height), font, defaultColor,
            selectedColor, drawLayer);
    }
}