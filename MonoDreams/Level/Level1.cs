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
            player.Set(new DynamicBody());
            player.Set(new Velocity{ Value = new Vector2(0, 300) });
            player.Set(new Position{ Value = new Point(1000, 200), Reminder = default });
            //player.Set<Solid>(default);
            player.Set(new DrawInfo
            {
                Color = Color.White,
                Destination = new Rectangle(0, 0, 90, 120)
            });
        }

        public static void Load(World world)
        {
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
                CreateBrick(world, 1700, 1110 - (i * 90));
            }
        }
    }
}