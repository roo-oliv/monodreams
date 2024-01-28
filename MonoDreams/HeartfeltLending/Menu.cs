using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using SpriteFontPlus;

namespace MonoDreams;

public class Menu
{
    public World World { get; }
    public ContentManager Content { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public ResolutionIndependentRenderer Renderer { get; }
    public SpriteFont Font { get; private set; }
    
    public Menu(GraphicsDevice graphicsDevice, ContentManager content, ResolutionIndependentRenderer renderer)
    {
        GraphicsDevice = graphicsDevice;
        Content = content;
        Renderer = renderer;
        LoadFonts();
        World = new World(100000);
        LoadWorld();
    }

    private void LoadFonts()
    {
        var fontBakeResult = TtfFontBaker.Bake(
            File.ReadAllBytes(@"/Users/rodrigooliveira/git/allrod5/monodreams/MonoDreams/Content/Text/Renogare-Regular.ttf"),
            100,
            4096,
            4096,
            new[] { CharacterRange.BasicLatin }
        );

        Font = fontBakeResult.CreateSpriteFont(GraphicsDevice);
    }

    private void LoadWorld()
    {
        var gameTitle = World.CreateEntity();
        gameTitle.Set(new Text
        {
            Value = "Heartfelt Lending",
            SpriteFont = Font,
            Color = Color.White,
            TextAlign = TextAlign.Center,
        });
        gameTitle.Set(new Position(new Vector2(0, -Renderer.VirtualHeight / 3.5f)));
    }
}