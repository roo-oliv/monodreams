using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;

namespace MonoDreams.Level
{
    public class Level1
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
            // player.Set(new Velocity{ Value = new Vector2(0, 300) });
            // player.Set(new Position(1000, 200));
            player.Set<Solid>(default);
            player.Set(new DrawInfo
            {
                Color = Color.White,
                Destination = new Rectangle(0, 0, 90, 120)
            });
        }

        public static void Load(World world)
        {
            for (var i = 0; i < 3; ++i)
            {
                CreateBrick(world, 1 + ((i + 6) * 181), 503);
            }
            
            for (var i = 0; i < 7; ++i)
            {
                CreateBrick(world, 1 + (i * 181), 1100);
            }
            
            for (var i = 0; i < 3; ++i)
            {
                CreateBrick(world, 1 + ((i + 7) * 181), 1200);
            }

            for (var i = 0; i < 2; ++i)
            {
                CreateBrick(world, 1701, 1109 - (i * 91));
            }

            for (var i = 0; i < 3; ++i)
            {
                CreateBrick(world, 1882, 1018 - (i * 91));
            }

            for (var i = 0; i < 4; ++i)
            {
                CreateBrick(world, 2063, 836 - (i * 91));
            }
            
            CreateBrick(world, 188, 503);
        }
    }
}