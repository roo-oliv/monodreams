using System;
using System.Collections;
using System.Collections.Generic;
using DefaultEcs;
using HeartfeltLending.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoGame.Extended.BitmapFonts;
using YamlDotNet.Serialization;

namespace HeartfeltLending;

public class DialogueScreen
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
    public BitmapFont DialogueFont { get; private set; }
    
    public DialogueScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, ResolutionIndependentRenderer renderer)
    {
        _game = game;
        GraphicsDevice = graphicsDevice;
        Content = content;
        Renderer = renderer;
        ExitGameCallback = _game.Exit;
        
        var deserializer = new Deserializer();
        var result = deserializer.Deserialize<List<Hashtable>>(content.Load<string>("Dialogue/FirstDialogue"));
        foreach (var item in result)
        {
            Console.WriteLine("Item:");
            foreach (DictionaryEntry entry in item)
            {
                Console.WriteLine("- {0} = {1}", entry.Key, entry.Value);
            }
        }
        
        LoadFonts();
        World = new World(100000);
        LoadWorld();
    }

    private void LoadFonts()
    {
        TitleFont = Content.Load<BitmapFont>("Text/Renogare-Regular-100p-white-shadow02-fnt");
        ButtonFont = Content.Load<BitmapFont>("Text/Renogare/Renogare-FS56-LH59-GrayF-BlackS1-fnt");
        DialogueFont = Content.Load<BitmapFont>("Text/Renogare/Renogare-FS48-LH50-WhiteF-fnt");
    }

    private void LoadWorld()
    {
        Background.Create(World, Content.Load<Texture2D>("Background/village-wallpaper"));
        Cursor.Create(World, Content.Load<Texture2D>("Other/Transition"));
        var entity = World.CreateEntity();
        var image = Content.Load<Texture2D>("Background/DialogueBox-300");
        entity.Set(new Position(new Vector2(-900, -500)));
        entity.Set(new DrawInfo(image, Color.White));
        var result = deserializer.Deserialize<List<MyObject>>(File.OpenText("myfile.yml"));
        Dialogue.Create(World, Content.Load<>());
        ProgressiveText.Create(World, "[color=Red]Este[/color] é um texto de [color=Red]exemplo[/color]... Aqui seria uma fala de [color=Red]uma[/color] personagem.", new Vector2(-800, -400), DialogueFont);
    }
}