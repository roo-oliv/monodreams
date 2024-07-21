using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoGame.Extended;
using MonoGame.Extended.BitmapFonts;
using MonoGame.Extended.TextureAtlases;

namespace MonoDreams.Objects.UI;

public class Button
{
    public static Entity Create(
        World world, string value, Action callback, Vector2 position, Point size, BitmapFont font, Color? defaultColor = null, Color? selectedColor = null, Texture2D buttonTexture = null, Thickness? padding = null, Rectangle? buttonRectangle = null, Enum drawLayer = null)
    {
        defaultColor ??= Color.White;
        selectedColor ??= Color.Black;
        var entity = world.CreateEntity();
        if (buttonTexture != null && buttonRectangle.HasValue && padding.HasValue)
        {
            // var ninePatchRegion = new NinePatchRegion2D(buttonTexture, padding.Value);
            // entity.Set(new CompositeDrawInfo(buttonTexture, ninePatchRegion, size, defaultColor.Value, drawLayer));
            var ninePatchInfo = new NinePatchInfo(
                new Rectangle(10, 10, 10, 10),
                new Rectangle(20, 10, 10, 10),
                new Rectangle(30, 10, 10, 10),
                new Rectangle(10, 20, 10, 10),
                new Rectangle(20, 20, 10, 10),
                new Rectangle(30, 20, 10, 10),
                new Rectangle(10, 30, 10, 10),
                new Rectangle(20, 30, 10, 10),
                new Rectangle(30, 30, 10, 10)
            );
            entity.Set(new DrawInfo(buttonTexture, size, buttonRectangle, defaultColor.Value, drawLayer, ninePatchInfo));
        }
        entity.Set(new Position(position));
        entity.Set(new Collidable(new Rectangle(size/new Point(-2, -2), size)));
        entity.Set(new SimpleText(value, font, defaultColor.Value, HorizontalAlign.Center, VerticalAlign.Center, drawLayer));
        entity.Set(new ButtonState(defaultColor.Value, selectedColor.Value, callback));
        return entity;
    }
    
    public static Entity Create(World world, string value, Action callback, Vector2 position, BitmapFont font,
        Color? defaultColor = null, Color? selectedColor = null, Texture2D buttonTexture = null, Thickness? padding = null, Rectangle? buttonRectangle = null, Enum drawLayer = null)
    {
        var size = font.MeasureString(value) + new Size2(5, 5);
        return Create(world, value, callback, position, new Point((int)size.Width, (int)size.Height), font, defaultColor, selectedColor, buttonTexture, padding, buttonRectangle, drawLayer);
    }
}