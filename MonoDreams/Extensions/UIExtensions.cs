#nullable enable
using System;
using DefaultEcs;
using Facebook.Yoga;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoGame.Extended;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Extensions;

public static class UIExtensions
{
    public static Entity CreateButton(
        this World world,
        Entity? layoutParent,
        string text,
        Action callback,
        Vector2 position,
        BitmapFont font,
        Color? defaultColor = null,
        Color? selectedColor = null,
        Enum? drawLayer = null)
    {
        var size = font.MeasureString(text) + new Size2(5, 5);
        return world.CreateButton(
            layoutParent,
            text,
            callback,
            position,
            new Point((int)size.Width, (int)size.Height),
            font,
            defaultColor,
            selectedColor,
            drawLayer);
    }

    public static Entity CreateButton(
        this World world,
        Entity? layoutParent,
        string text,
        Action callback,
        Vector2 position,
        Point size,
        BitmapFont font,
        Color? defaultColor = null,
        Color? selectedColor = null,
        Enum? drawLayer = null)
    {
        defaultColor ??= Color.White;
        selectedColor ??= Color.Black;
        
        var buttonEntity = world.CreateLayoutEntity(
            parent: layoutParent,
            yogaNode: new YogaNode
            {
                Width = size.X,
                Height = size.Y,
                Margin = 20,
            });
        
        buttonEntity.Set(new Position(position));
        buttonEntity.Set(new Collidable(new Rectangle(size / new Point(-2, -2), size)));
        buttonEntity.Set(new SimpleText(text, font, defaultColor.Value, HorizontalAlign.Center, VerticalAlign.Center, drawLayer));
        buttonEntity.Set(new ButtonState(defaultColor.Value, selectedColor.Value, callback));
        
        return buttonEntity;
    }
}