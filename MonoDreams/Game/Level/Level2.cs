using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.EntityFactory;
using MonoDreams.Sprite;

namespace MonoDreams.Level;

public class Level2
{
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly PlayerFactory _playerFactory;

    private const int Scale = 10;

    public Level2(World world, ContentManager content)
    {
        _world = world;
        _content = content;
        _playerFactory = new PlayerFactory(_world, _content, Scale);
    }

    private static void CreateBrick(World world, ContentManager content, int x, int y)
    {
        var brick = world.CreateEntity();
        brick.Set(new Position(new Vector2(x, y)));
        
        brick.Set(new DrawInfo
        {
            SpriteSheet = content.Load<Texture2D>("Terrain/Terrain (16x16)"),
            Source = new Rectangle(16 * 7, 16 * 4, 16, 16),
            Color = Color.White,
            Destination = new Rectangle(x, y, 5 * Scale, 5 * Scale)
        });
        brick.Set<Solid>(default);
    }

    private void CreateBackground(int x, int y)
    {
        var bg = _world.CreateEntity();
        bg.Set(new Position(new Vector2(x, y)));
        bg.Set(new DrawInfo
        {
            SpriteSheet = _content.Load<Texture2D>("Background/Green"),
            Source = new Rectangle(0, 0, 64, 64),
            Color = Color.White,
            Destination = new Rectangle(0, 0, 10 * Scale, 10 * Scale)
        });
    }

    // public static void CreatePlayer(World world, ContentManager content)
    // {
    //     var player = world.CreateEntity();
    //     player.Set(new Position(new Vector2(60 * Scale, 20 * Scale)));
    //     player.Set(new DrawInfo
    //     {
    //         Sprite = content.Load<Texture2D>("Main Characters/Virtual Guy/Idle (32x32)"),
    //         Color = Color.White,
    //         Destination = new Rectangle(0, 0, 9 * Scale, 12 * Scale)
    //     });
    //     player.Set(new PlayerInput());
    //     player.Set(new DynamicBody());
    //     player.Set(new MovementController());
    //     player.Set<Solid>(default);
    // }
        
    public void Load()
    {
        for (var i = -20; i < 90; ++i)
        {
            for (var j = -20; j < 30; ++j)
            {
                CreateBackground(i*10 * Scale, j*10 * Scale);
            }
        }

        for (var i = 0; i < 84; ++i)
        {
            CreateBrick(_world, _content, i*5 * Scale, 190 * Scale);
        }
            
        for (var i = 0; i < 42; ++i)
        {
            CreateBrick(_world, _content, 0, i*5 * Scale);
        }
            
        for (var i = 0; i < 40; ++i)
        {
            CreateBrick(_world, _content, 372 * Scale, i*5 * Scale);
        }
            
        for (var i = 0; i < 8; ++i)
        {
            CreateBrick(_world, _content, 316 * Scale + i*5 * Scale, 150 * Scale);
        }

        for (var i = 0; i < 4; ++i)
        {
            CreateBrick(_world, _content, 280 * Scale + i*5 * Scale, 110 * Scale);
        }

        for (var i = 0; i < 20; ++i)
        {
            CreateBrick(_world, _content, 170 * Scale + i*5 * Scale, 70 * Scale);
        }
        
        _playerFactory.CreatePlayer(new Vector2(60 * Scale, 20 * Scale));
    }
}