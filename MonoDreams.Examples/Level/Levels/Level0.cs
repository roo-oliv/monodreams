using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Objects;
using MonoDreams.Examples.Screens;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;
using Point = Microsoft.Xna.Framework.Point;

namespace MonoDreams.Examples.Level.Levels;

public class Level0(
    ContentManager content,
    GraphicsDevice graphicsDevice,
    SpriteBatch spriteBatch,
    (RenderTarget2D main, RenderTarget2D ui) renderTargets) : ILevel
{
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
            
            // StaticText.Create(world, font, "Move with Arrow Keys or A and D", Color.White, new Vector2(20 * 12, 20 * 24), DrawLayer.Text);
            // StaticText.Create(world, font, "Jump with Spacebar", Color.White, new Vector2(20 * 12, 20 * 25), DrawLayer.Text);
            // StaticText.Create(world, font, "Hold Jump to go farther", Color.White, new Vector2(20 * 21, 20 * 13), DrawLayer.Text);
        }

        Dialogue.Create(world, "Lorem ipsum dolor sit amet Lorem ipsum dolor sit amet Lorem ipsum dolor sit amet Lorem ipsum dolor sit amet", _emoteTexture, font, _dialogBox, renderTargets.ui, graphicsDevice, DreamGameScreen.DrawLayer.UIElements);
        
        // PlayerState.Create(world, Constants.WorldGravity, _square, new Vector2(-900, 370), DrawLayer.PlayerState);
        // LoadTiles(world);
        // LevelBoundaries.Create(world, _square, renderer);
    }
    
    private void LoadTiles(World world)
    {
        float factor = 0.3f;
        // ceiling
        Tile.Create(world, _square, new Vector2(-1000 * factor, -550 * factor), new Point((int)(2000 * factor), (int)(50 * factor)), renderTargets.main, TileType.Default);
        
        // left wall
        Tile.Create(world, _square, new Vector2(-1000 * factor, -500 * factor), new Point((int)(70 * factor), (int)(900 * factor)), renderTargets.main, TileType.Default);
        
        // initial floor
        Tile.Create(world, _square, new Vector2(-1000 * factor, 400 * factor), new Point((int)(670 * factor), (int)(100 * factor)), renderTargets.main, TileType.Default);
        
        // small hill
        Tile.Create(world, _square, new Vector2(-700 * factor, 300 * factor), new Point((int)(220 * factor), (int)(200 * factor)), renderTargets.main, TileType.Default);
        
        // left cliff
        Tile.Create(world, _square, new Vector2(-480 * factor, 200 * factor), new Point((int)(150 * factor), (int)(400 * factor)), renderTargets.main, TileType.Default);
        
        // right cliff
        Tile.Create(world, _square, new Vector2(-130 * factor, 200 * factor), new Point((int)(500 * factor), (int)(520 * factor)), renderTargets.main, TileType.Default);
        
        // big hill
        Tile.Create(world, _square, new Vector2(350 * factor, -300 * factor), new Point((int)(150 * factor), (int)(800 * factor)), renderTargets.main, TileType.Default);
        
        // big upside hill
        Tile.Create(world, _square, new Vector2(-100 * factor, -550 * factor), new Point((int)(300 * factor), (int)(600 * factor)), renderTargets.main, TileType.Default);
        
        // finish floor
        Tile.Create(world, _square, new Vector2(350 * factor, 400 * factor), new Point((int)(700 * factor), (int)(100 * factor)), renderTargets.main, TileType.Default);
        
        // right wall
        Tile.Create(world, _square, new Vector2(800 * factor, -550 * factor), new Point((int)(200 * factor), (int)(850 * factor)), renderTargets.main, TileType.Default);
        
        // objective
        Tile.Create(world, _square, new Vector2(950 * factor, 300 * factor), new Point((int)(200 * factor), (int)(150 * factor)), renderTargets.main, TileType.Objective);
    }
}