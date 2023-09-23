using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;

namespace MonoDreams.Level;

public class Level1
{
    private static void CreateBrick(World world, int x, int y)
    {
        Entity brick = world.CreateEntity();
        brick.Set(new DrawInfo
        {
            Color = Color.Red,
            Destination = new Rectangle(x, y, 18, 9)
        });
        brick.Set<Solid>(default);
    }

    public static void CreatePlayer(World world)
    {
        var player = world.CreateEntity();
        player.Set(new Position(new Vector2(60, 20)));
        player.Set(new PlayerInput());
        player.Set(new DynamicBody());
        player.Set(new MovementController());
        // player.Set(new Velocity{ Value = new Vector2(0, 300) });
        // player.Set(new Position(1000, 200));
        player.Set<Solid>(default);
        player.Set(new DrawInfo
        {
            Color = Color.White,
            Destination = new Rectangle(0, 0, 9, 12)
        });
    }

    public static void Load(World world)
    {
        for (var i = 0; i < 3; ++i)
        {
            CreateBrick(world, 1 + ((i + 6) * 18), 50);
        }
            
        for (var i = 0; i < 7; ++i)
        {
            CreateBrick(world, 1 + (i * 18), 110);
        }
            
        for (var i = 0; i < 3; ++i)
        {
            CreateBrick(world, 1 + ((i + 7) * 18), 120);
        }

        for (var i = 0; i < 2; ++i)
        {
            CreateBrick(world, 170, 110 - (i * 9));
        }

        for (var i = 0; i < 3; ++i)
        {
            CreateBrick(world, 188, 101 - (i * 9));
        }

        for (var i = 0; i < 4; ++i)
        {
            CreateBrick(world, 206, 83 - (i * 9));
        }
            
        CreateBrick(world, 18, 50);
    }
}