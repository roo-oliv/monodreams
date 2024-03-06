using System;
using DefaultEcs;
using HeartfeltLending.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Renderer;
using MonoGame.Extended.BitmapFonts;

namespace HeartfeltLending;

public class Menu
{
    private readonly Game _game;
    public static string GameTitleString = "Heartfelt Lending";
    public static string StartGameString = "Start Game";
    public static string OptionsString = "Options";
    public static string ExitGameString = "Exit Game";
    public static Color GameTitleColor = new(181,213,76);
    public static Color ButtonDefaultColor = Color.White;
    public static Color ButtonSelectedColor = new(89,111,59);
    public Action StartGameCallback;
    public Action OptionsCallback;
    public Action ExitGameCallback;
    public World World { get; }
    public ContentManager Content { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public ResolutionIndependentRenderer Renderer { get; }
    public BitmapFont TitleFont { get; private set; }
    public BitmapFont ButtonFont { get; private set; }
    
    public Menu(Game game, GraphicsDevice graphicsDevice, ContentManager content, ResolutionIndependentRenderer renderer)
    {
        _game = game;
        GraphicsDevice = graphicsDevice;
        Content = content;
        Renderer = renderer;
        ExitGameCallback = _game.Exit;
        LoadFonts();
        World = new World(100000);
        LoadWorld();
    }

    private void LoadFonts()
    {
        TitleFont = Content.Load<BitmapFont>("Text/Renogare-Regular-100p-white-shadow02-fnt");
        ButtonFont = Content.Load<BitmapFont>("Text/Renogare-Regular-100p-white-stroke3-fnt");
    }

    private void LoadWorld()
    {
        Background.Create(World, Content.Load<Texture2D>("Background/village-wallpaper"));
        Cursor.Create(World, Content.Load<Texture2D>("Other/Transition"));
        StaticText.Create(World, GameTitleString, new Vector2(0, -Renderer.VirtualHeight / 3.5f), TitleFont, GameTitleColor);
        Button.Create(World, StartGameString, ExitGameCallback, new Vector2(0, 0), ButtonFont, ButtonDefaultColor, ButtonSelectedColor);
        Button.Create(World, OptionsString, ExitGameCallback, new Vector2(0, 150), ButtonFont, ButtonDefaultColor, ButtonSelectedColor);
        Button.Create(World, ExitGameString, ExitGameCallback, new Vector2(0, 300), ButtonFont, ButtonDefaultColor, ButtonSelectedColor);
    }
}