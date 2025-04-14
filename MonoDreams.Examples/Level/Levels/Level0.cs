using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Dialogue;
using MonoDreams.Examples.Objects;
using MonoDreams.Examples.Screens;
using MonoDreams.Util;
using MonoDreams.YarnSpinner;
using MonoGame.Extended.BitmapFonts;
using Yarn;
using Point = Microsoft.Xna.Framework.Point;

namespace MonoDreams.Examples.Level.Levels;

public class Level0(
    ContentManager content,
    GraphicsDevice graphicsDevice,
    SpriteBatch spriteBatch,
    (RenderTarget2D main, RenderTarget2D ui) renderTargets) : ILevel
{
    public World World { get; private set; }
    public ContentManager Content { get; } = content;
    public GraphicsDevice GraphicsDevice { get; } = graphicsDevice;
    public SpriteBatch SpriteBatch { get; } = spriteBatch;
    
    private readonly Texture2D _square = content.Load<Texture2D>("square");
    private readonly BitmapFont font = content.Load<BitmapFont>("Fonts/kaph-regular-72px-white-fnt");
    private readonly Texture2D _dialogBox = content.Load<Texture2D>("Dialouge UI/dialog box");
    private readonly Texture2D _emoteTexture = content.Load<Texture2D>("Dialouge UI/Emotes/Teemo Basic emote animations sprite sheet").Crop(new Rectangle(0, 0, 32, 32), graphicsDevice);

    public const string Layout = @"
    .*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*.
    .*..*..*.....*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*.
    .*..*..W........W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W........W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W........W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W........W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W........W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W........W..W..W..W..W..W..W.....................................................W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W..@.....W..W..W..W..W..W..W.....................................................W..W..W..W..........................W..*..*.
    .*..*..W..............W..W..W..W..W.....................................................W..W..W..W..........................W..*..*.
    .*..*..W..............W..W..W..W..W.....................................................W..W..W..W..........................W..*..*.
    .*..*..W..............W..W..W..W..W.....................................................W..W..W..W..........................W..*..*.
    .*..*..W..............W..W..W..W..W.....................................................W..W..W..W..........................O..*..*.
    .*..*..W..............W..W..W...........................................................W..W..W..W..........................O..*..*.
    .*..*..W..............W..W..W...........................................................W..W..W..W.................W..W..W..W..*..*.
    .*..*..W..............W..W..W.................................................................W..W.................W..W..W..W..*..*.
    .*..*..W..............W..W..W.................................................................W..W..W..W..............W..W..W..*..*.
    .*..*..W..............W..W..W.................................................................W..W..W.......................W..*..*.
    .*..*..W..............W..W..W....................W..W....................W..W..W..............W..W..W..W..W.................W..*..*.
    .*..*..W.........................................W..W....................W..W..W............................................W..*..*.
    .*..*..W...................................W..W..W..W....................W..W..W...................................W..W..W..W..*..*.
    .*..*..W...................................W..W..W..W....................W..W..W..W................................W..W..W..W..*..*.
    .*..*..W..........................W..W..W..W..W..W..W....................W..W..W..W.................W..W..W..............W..W..*..*.
    .*..*..W...........W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W........W..W..W..............W..W..*..*.
    .*..*..W...........W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W.............................W..W..*..*.
    .*..*..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W....................W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
    .*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*.
    .*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*..*.
    ";

    public void Load(World world)
    {
        World = world;
        var levelLayout = string.Concat(Layout.Where(c => !char.IsWhiteSpace(c)));
        
        int columns = 44;
        int rows = 32;
        
        for (int i = 0; i < levelLayout.Length; i += 3)
        {
            int row = 20 * (i / (columns * 3));
            int col = 20 * ((i / 3) % columns);
            string chunk = levelLayout.Substring(i, 3);

            switch (chunk)
            {
                case "...":
                    break;
                case ".@.":
                    Player.Create(world, _square, new Vector2(col+7, row+7), renderTargets.main, DreamGameScreen.DrawLayer.Player);
                    break;
                case ".*.":
                    Tile.Create(
                        world, _square,
                        new Vector2(col, row), new Point(20, 20), renderTargets.main,
                        TileType.Deadly,
                        DreamGameScreen.DrawLayer.Level);
                    break;
                case ".W.":
                    Tile.Create(
                        world, _square,
                        new Vector2(col, row), new Point(20, 20), renderTargets.main,
                        TileType.Default,
                        DreamGameScreen.DrawLayer.Level);
                    break;
                case ".O.":
                    Tile.Create(
                        world, _square,
                        new Vector2(col, row), new Point(20, 20), renderTargets.main,
                        TileType.Objective,
                        DreamGameScreen.DrawLayer.Level);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(chunk), chunk, null);
            }
        }
        
        var dialogueZone = world.CreateEntity();
        dialogueZone.Set(new Position(new Vector2(200, 200)));
        dialogueZone.Set(new BoxCollider(new Rectangle(0, 0, 64, 64), passive: true));
        dialogueZone.Set(new DrawInfo(renderTargets.main, _square, new Point(64, 64), layer: DreamGameScreen.DrawLayer.Level));
        dialogueZone.Set(new DialogueTrigger
        {
            Type = TriggerType.Proximity,
            StartNode = "AreaDiscovery",
            ProximityRadius = 5.0f,
            OneTimeOnly = true,
        });
        // dialogueZone.Set(new DialogueComponent
        // {
        //     Program = Content.Load<YarnProgram>("Dialogues/area_discovery"),
        //     DefaultStartNode = "AreaDiscovery",
        //     Language = "en",
        // });

        // Zone.Create(this, new Vector2(100, 180), new Point(100, 100), DialogueScript.HelloWorld);
        //
        // var dialogueRunner = new DialogueRunner();
        // var yarnNodeName = "HelloWorld";
        // var zonePosition = new Vector2(100, 180);
        // var zoneCollider = new Rectangle(0, 0, 40, 40);
        // var variableStorage = new InMemoryVariableStorage();
        // var dialogue = new Yarn.Dialogue(variableStorage)
        // {
        //     LogDebugMessage = Console.WriteLine,
        //     LogErrorMessage = Console.WriteLine,
        //     LineHandler = line => Console.WriteLine(dialogueRunner.GetLocalizedTextForLine(line)),
        //     CommandHandler = command => Console.WriteLine(command),
        //     OptionsHandler = options =>
        //     {
        //         for (var i = 0; i < options.Options.Length; i++)
        //         {
        //             var line = options.Options[i].Line;
        //             Console.WriteLine($"{i}: {dialogueRunner.GetLocalizedTextForLine(line)}");
        //         }
        //     },
        //     NodeStartHandler = node => Console.WriteLine(node),
        //     NodeCompleteHandler = node => Console.WriteLine(node),
        //     DialogueCompleteHandler = () => Console.WriteLine("Dialogue complete"),
        // };
        // var rawProgram = Content.Load<YarnProgram>("Dialogues/hello_world");
        // dialogue.SetProgram(rawProgram.GetProgram());
        // dialogueRunner.AddStringTable(rawProgram);
        // var currentNode = dialogue.CurrentNode;


        // DialogueZone.Create(world, yarnNodeName, zonePosition, zoneCollider, _emoteTexture, font, _dialogBox, renderTargets.ui, graphicsDevice, DreamGameScreen.DrawLayer.UIElements);

        // Objects.Dialogue.Create(world, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Proin pellentesque consequat tempor. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas.", _emoteTexture, font, _dialogBox, renderTargets.ui, graphicsDevice, DreamGameScreen.DrawLayer.UIElements);
    }
}