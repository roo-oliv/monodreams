using System.IO;
using DefaultEcs;
using HeartfeltLending.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.InputState;
using MonoDreams.Renderer;
using MonoGame.Extended;
using MonoGame.Extended.BitmapFonts;
using SpriteFontPlus;

namespace HeartfeltLending;

public class Menu
{
    public static string GameTitleString = "Heartfelt Lending";
    public static string StartGameString = "Start Game";
    public World World { get; }
    public ContentManager Content { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public ResolutionIndependentRenderer Renderer { get; }
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
    }

    private void LoadWorld()
    {
        var cursor = Cursor.Create(World, Content.Load<Texture2D>("Other/Transition"));
        var gameTitle = StaticText.Create(World, GameTitleString, new Vector2(0, -Renderer.VirtualHeight / 3.5f), TitleFont, Color.LimeGreen);
        var startGameSize = TitleFont.MeasureString(StartGameString) + new Size2(5, 5);
        var startGame = Button.Create(World, StartGameString, new Vector2(0, 0), new Point((int)startGameSize.Width, (int)startGameSize.Height), TitleFont, Color.Peru);
    }
}