using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;

namespace MonoDreams.Level;

public class Level2
{

    private const int Scale = 2;
    private static void CreateBrick(World world, int x, int y)
    {
        Entity brick = world.CreateEntity();
        brick.Set(new DrawInfo
        {
            Color = Color.Red,
            Destination = new Rectangle(x, y, 18 * Scale, 9 * Scale)
        });
        brick.Set<Solid>(default);
    }

    public static void CreatePlayer(World world)
    {
        var player = world.CreateEntity();
        player.Set(new PlayerInput());
        player.Set(new DynamicBody(60 * Scale, 20 * Scale));
        player.Set(new MovementController());
        player.Set<Solid>(default);
        player.Set(new DrawInfo
        {
            Color = Color.White,
            Destination = new Rectangle(0, 0, 9 * Scale, 12 * Scale)
        });
    }
        
    public static void Load(World world)
    {
        for (var i = 0; i < 21; ++i)
        {
            CreateBrick(world, i * 18 * Scale, 190 * Scale);
        }
            
        for (var i = 0; i < 21; ++i)
        {
            CreateBrick(world, 0, i*9 * Scale);
        }
            
        for (var i = 0; i < 20; ++i)
        {
            CreateBrick(world, 372 * Scale, i*9 * Scale);
        }
            
        for (var i = 0; i < 2; ++i)
        {
            CreateBrick(world, 316 * Scale + i * 18 * Scale, 150 * Scale);
        }
            
        CreateBrick(world, 280 * Scale, 110 * Scale);
            
        for (var i = 0; i < 5; ++i)
        {
            CreateBrick(world, 170 * Scale + i * 18 * Scale, 70 * Scale);
        }
    }
}