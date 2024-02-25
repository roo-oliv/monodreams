using System.IO;
using DefaultEcs;
using HeartfeltLending.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.InputState;
using MonoDreams.Renderer;
using MonoGame.Extended.BitmapFonts;
using SpriteFontPlus;

namespace HeartfeltLending;

public class Menu
{
    public World World { get; }
    public ContentManager Content { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public ResolutionIndependentRenderer Renderer { get; }
    // public SpriteFont LargeFont { get; private set; }
    // public SpriteFont RegularFont { get; private set; }
    public BitmapFont TitleFont { get; private set; }
    
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
        TitleFont = Content.Load<BitmapFont>("Text/Renogare-Regular-100p-white-shadow02-fnt");
        
        // var largeFontBakeResult = TtfFontBaker.Bake(
        //     File.ReadAllBytes("/Users/rodrigooliveira/git/allrod5/monodreams/MonoDreams/Content/Text/Renogare-Regular.ttf"),
        //     150,
        //     1024,
        //     1024,
        //     new[] { CharacterRange.BasicLatin }
        // );
        //
        // LargeFont = largeFontBakeResult.CreateSpriteFont(GraphicsDevice);
        //
        // var regularFontBakeResult = TtfFontBaker.Bake(
        //     File.ReadAllBytes("/Users/rodrigooliveira/git/allrod5/monodreams/MonoDreams/Content/Text/Renogare-Regular.ttf"),
        //     100,
        //     1024,
        //     1024,
        //     new[] { CharacterRange.BasicLatin }
        // );
        //
        // RegularFont = regularFontBakeResult.CreateSpriteFont(GraphicsDevice);
    }

    private void LoadWorld()
    {
        var cursor = Cursor.Create(World, Content.Load<Texture2D>("Other/Transition"));
        var gameTitle = StaticText.Create(World, "Heartfelt Lending", new Vector2(0, -Renderer.VirtualHeight / 3.5f), TitleFont, Color.LimeGreen);
        var startGame = StaticText.Create(World, "Start Game", new Vector2(0, 0), TitleFont, Color.Peru);
    }
}