using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Objects;

public static class Dialogue
{
    public static Entity Create(World world, String text, Texture2D emoteTexture, BitmapFont font, Texture2D dialogBoxTexture, RenderTarget2D renderTarget, GraphicsDevice graphicsDevice, Enum boxDrawLayer = null, Enum textDrawLayer = null)
    {
        var textEntity = world.CreateEntity();
        textEntity.Set(new EntityInfo(EntityType.Interface));
        textEntity.Set(new DynamicText(renderTarget, text, font, drawLayer: textDrawLayer, maxLineWidth: 2800));
        textEntity.Set(new Transform(new Vector2(-1150, -900)));

        var dialogBox = world.CreateEntity();
        dialogBox.Set(new EntityInfo(EntityType.Interface));
        dialogBox.Set(new Transform(new Vector2(-1400, -1100)));
        dialogBox.Set(
            new DrawInfo(
                renderTarget,
                dialogBoxTexture,
                new Point(3300, 720),
                layer: boxDrawLayer,
                ninePatchInfo: new NinePatchInfo(
                    96,
                    new Rectangle(0, 0, 23, 23),
                    new Rectangle(23, 0, 1, 23),
                    new Rectangle(24, 0, 23, 23),
                    new Rectangle(0, 23, 23, 1),
                    new Rectangle(23, 23, 1, 1),
                    new Rectangle(24, 23, 23, 1),
                    new Rectangle(0, 24, 23, 23),
                    new Rectangle(23, 24, 1, 23),
                    new Rectangle(24, 24, 23, 23)
                    ),
                graphicsDevice: graphicsDevice
                )
            );

        var emote = world.CreateEntity();
        emote.Set(new EntityInfo(EntityType.Interface));
        emote.Set(new Transform(new Vector2(-1900, -1000)));
        emote.Set(new DrawInfo(renderTarget, emoteTexture, new Point(550, 550), layer: boxDrawLayer));
        
        return textEntity;
    }
}