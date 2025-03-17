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
    .*..*..W........W..W..W..W..W..W..W..................................................W..W..W..W..W..W..W..W..W..W..W..W..W..*..*.
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

        // Create dialogue zones
        CreateDialogueZones(world);
    }

    private void CreateDialogueZones(World world)
    {
        var dialogueZoneEntity = world.CreateEntity();
        dialogueZoneEntity.Set(new EntityInfo(EntityType.Zone));
        dialogueZoneEntity.Set(new Position(new Vector2(100, 180)));
        dialogueZoneEntity.Set(new DrawInfo(renderTargets.ui, _square, new Point(40, 40), layer: DreamGameScreen.DrawLayer.UIElements));
        dialogueZoneEntity.Set(new BoxCollider(new Rectangle(0, 0, 40, 40), passive: true));
        dialogueZoneEntity.Set(new DialogueZoneComponent("HelloWorld", oneTimeOnly: true, autoStart: true));
        
        // Create a second dialogue zone that checks for variable state
        var worldExploredZoneEntity = world.CreateEntity();
        worldExploredZoneEntity.Set(new EntityInfo(EntityType.Zone));
        worldExploredZoneEntity.Set(new Position(new Vector2(580, 180))); // Place this further to the right
        worldExploredZoneEntity.Set(new DrawInfo(renderTargets.ui, _square, new Point(40, 40), layer: DreamGameScreen.DrawLayer.UIElements));
        worldExploredZoneEntity.Set(new BoxCollider(new Rectangle(0, 0, 40, 40), passive: true));
        worldExploredZoneEntity.Set(new DialogueZoneComponent("WorldExplored", oneTimeOnly: false, autoStart: true));
    }
}