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
            Entity player = world.CreateEntity();
            player.Set(new PlayerInput());
            player.Set(new DynamicBody());
            player.Set(new Velocity{ Value = new Vector2(0, 0)});
            player.Set(new Position{ Value = new Point(1000, 1000)});
            player.Set<Solid>(default);
            player.Set(new DrawInfo
            {
                Color = Color.White,
                Destination = new Rectangle(0, 550, 90, 120)
            });
        }

        public static void Load(World world)
        {
            for (int i = 0; i < 21; ++i)
            {
                CreateBrick(world, 1 + (i * 181), 1600);
            }
        }
    }
}