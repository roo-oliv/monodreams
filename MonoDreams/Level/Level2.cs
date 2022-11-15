using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;

namespace MonoDreams.Level
{
    public class Level2
    {
        private static void CreateBrick(World world, int x, int y)
        {
            Entity brick = world.CreateEntity();
            brick.Set(new DrawInfo
            {
                Color = Color.Red,
                Destination = new Rectangle(x, y, 180, 90)
            });
            brick.Set<Solid>(default);
        }
        
        public static void CreatePlayer(World world)
        {
            var player = world.CreateEntity();
            player.Set(new PlayerInput());
            player.Set(new DynamicBody(600, 200));
            player.Set(new MovementController());
            player.Set<Solid>(default);
            player.Set(new DrawInfo
            {
                Color = Color.White,
                Destination = new Rectangle(0, 0, 90, 120)
            });
        }
        
        public static void Load(World world)
        {
            for (var i = 0; i < 21; ++i)
            {
                CreateBrick(world, i * 181, 1900);
            }
            
            for (var i = 0; i < 21; ++i)
            {
                CreateBrick(world, 0, i*91);
            }
            
            for (var i = 0; i < 20; ++i)
            {
                CreateBrick(world, 3720, i*91);
            }
            
            for (var i = 0; i < 2; ++i)
            {
                CreateBrick(world, 3160 + i * 181, 1500);
            }
            
            CreateBrick(world, 2800, 1100);
            
            for (var i = 0; i < 5; ++i)
            {
                CreateBrick(world, 1700 + i * 181, 700);
            }
        }
    }
}
