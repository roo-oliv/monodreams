using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Objects;

public static class Dialogue
{
    public static Entity Create(World world, String text, BitmapFont font, Texture2D dialogBoxTexture, Enum drawLayer = null)
    {
        var textEntity = world.CreateEntity();
        textEntity.Set(new DynamicText(text, font));
        textEntity.Set(new Position(new Vector2(-1000, 850)));
        
        var dialogBox = world.CreateEntity();
        dialogBox.Set(new Position(new Vector2(-1300, 700)));
        dialogBox.Set(new DrawInfo(dialogBoxTexture, new Point(3300, 720), layer: drawLayer));
        
        return textEntity;
    }
}