using DefaultEcs;
using Iguina.Defs;
using Iguina.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Objects;
using MonoDreams.Examples.Screens;
using MonoDreams.Renderer;
using MonoGame.Extended.BitmapFonts;
using Color = Iguina.Defs.Color;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Iguina.Defs.Rectangle;

namespace MonoDreams.Examples.Level.Levels;

public class Level0(ContentManager content, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch) : ILevel
{
    private readonly Texture2D _square = content.Load<Texture2D>("square");
    private readonly BitmapFont font = content.Load<BitmapFont>("Fonts/kaph-regular-72px-white-fnt");
    private readonly Texture2D _dialogBox = content.Load<Texture2D>("Dialouge UI/dialog box big");

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
                    Player.Create(world, _square, new Vector2(col+7, row+7));
                    break;
                case ".*.":
                    Tile.Create(
                        world, _square,
                        new Vector2(col, row), new Point(20, 20),
                        TileType.Deadly);
                    break;
                case ".W.":
                    Tile.Create(
                        world, _square,
                        new Vector2(col, row), new Point(20, 20),
                        TileType.Default);
                    break;
                case ".O.":
                    Tile.Create(
                        world, _square,
                        new Vector2(col, row), new Point(20, 20),
                        TileType.Objective);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(chunk), chunk, null);
            }
            
            // StaticText.Create(world, font, "Move with Arrow Keys or A and D", Color.White, new Vector2(20 * 12, 20 * 24), DrawLayer.Text);
            // StaticText.Create(world, font, "Jump with Spacebar", Color.White, new Vector2(20 * 12, 20 * 25), DrawLayer.Text);
            // StaticText.Create(world, font, "Hold Jump to go farther", Color.White, new Vector2(20 * 21, 20 * 13), DrawLayer.Text);
        }

        // Dialogue.Create(world, "Lorem ipsum dolor sit amet Lorem ipsum dolor sit amet Lorem ipsum dolor sit amet Lorem ipsum dolor sit amet", font, _dialogBox, OptionsMenuScreen.DrawLayer.Buttons);
        
        // PlayerState.Create(world, Constants.WorldGravity, _square, new Vector2(-900, 370), DrawLayer.PlayerState);
        // LoadTiles(world);
        // LevelBoundaries.Create(world, _square, renderer);

        var iguinaInterface = new IguinaInterface("../../../Assets/DefaultTheme", content, spriteBatch, graphicsDevice);
        var ui = iguinaInterface.UserInterface;
        var panel = new Panel(ui)
        {
            Anchor = Anchor.TopRight,
        };
        panel.Size.SetPixels(1600, 200);
        panel.Offset.SetPixels(32, 32);
        ui.Root.AddChild(panel);

        // Draw the texture
        var textureId = "Dialouge UI/Emotes/Teemo Basic emote animations sprite sheet.png";
        var sourceRect = new Rectangle(0, 0, 32, 32); // Source rectangle in the texture
        var destRect = new Rectangle(150, 150, 200, 200); // Destination rectangle on the panel
        var color = new Color(255, 255, 255, 255); // White color tint
        
        var dynParagraph = new Paragraph(ui)
        {
            Anchor = Anchor.AutoLTR,
        };
        iguinaInterface.DynamicParagraphs.Add(
            ("Hello World! This is a great example to test as placeholder dialogue text" +
             " that may overflow the dialogue box! Thank you!",
                dynParagraph));
        panel.AddChild(dynParagraph);
            
        {
            var enableDebugModeBtn = new Button(ui, "Debug Mode")
            {
                ToggleCheckOnClick = true,
                Anchor = Anchor.TopRight
            };
            enableDebugModeBtn.Events.OnValueChanged = _ =>
            {
                ui.DebugRenderEntities = enableDebugModeBtn.Checked;
            };
            enableDebugModeBtn.Size.X.SetPixels(400);
            ui.Root.AddChild(enableDebugModeBtn);
        }
        
        var uiEntity = world.CreateEntity();
        uiEntity.Set(iguinaInterface);
    }
    
    private void LoadTiles(World world)
    {
        float factor = 0.3f;
        // ceiling
        Tile.Create(world, _square, new Vector2(-1000 * factor, -550 * factor), new Point((int)(2000 * factor), (int)(50 * factor)), TileType.Default);
        
        // left wall
        Tile.Create(world, _square, new Vector2(-1000 * factor, -500 * factor), new Point((int)(70 * factor), (int)(900 * factor)), TileType.Default);
        
        // initial floor
        Tile.Create(world, _square, new Vector2(-1000 * factor, 400 * factor), new Point((int)(670 * factor), (int)(100 * factor)), TileType.Default);
        
        // small hill
        Tile.Create(world, _square, new Vector2(-700 * factor, 300 * factor), new Point((int)(220 * factor), (int)(200 * factor)), TileType.Default);
        
        // left cliff
        Tile.Create(world, _square, new Vector2(-480 * factor, 200 * factor), new Point((int)(150 * factor), (int)(400 * factor)), TileType.Default);
        
        // right cliff
        Tile.Create(world, _square, new Vector2(-130 * factor, 200 * factor), new Point((int)(500 * factor), (int)(520 * factor)), TileType.Default);
        
        // big hill
        Tile.Create(world, _square, new Vector2(350 * factor, -300 * factor), new Point((int)(150 * factor), (int)(800 * factor)), TileType.Default);
        
        // big upside hill
        Tile.Create(world, _square, new Vector2(-100 * factor, -550 * factor), new Point((int)(300 * factor), (int)(600 * factor)), TileType.Default);
        
        // finish floor
        Tile.Create(world, _square, new Vector2(350 * factor, 400 * factor), new Point((int)(700 * factor), (int)(100 * factor)), TileType.Default);
        
        // right wall
        Tile.Create(world, _square, new Vector2(800 * factor, -550 * factor), new Point((int)(200 * factor), (int)(850 * factor)), TileType.Default);
        
        // objective
        Tile.Create(world, _square, new Vector2(950 * factor, 300 * factor), new Point((int)(200 * factor), (int)(150 * factor)), TileType.Objective);
    }
}