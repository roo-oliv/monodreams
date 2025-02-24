using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.Examples.Objects;

public class Tile
{
    public static Color DefaultColor = new(32, 40, 51);
    public static Color DefaultReactiveColor = Color.Aquamarine;
    public static Color ActiveReactiveColor = Color.MediumAquamarine;
    public static Color DeadlyColor = Color.OrangeRed;
    public static Color ObjectiveColor = Color.Gold;
    
    public static Entity Create(World world, Texture2D texture, Vector2 position, Point size, RenderTarget2D renderTarget, TileType type = TileType.Default, Enum? drawLayer = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new Position(position));
        entity.Set(new BoxCollider( new Rectangle(Point.Zero, size), passive: true));
        entity.Set(new RigidBody(isKinematic: true, gravityActive: false));
        var color = type switch
        {
            TileType.Default => DefaultColor,
            TileType.Reactive => DefaultReactiveColor,
            TileType.Deadly => DeadlyColor,
            TileType.Objective => ObjectiveColor,
            _ => DefaultColor
        };
        entity.Set(new DrawInfo(renderTarget, texture, size, color: color, layer: drawLayer));
        // switch (type)
        // {
        //     case TileType.Reactive:
        //         entity.Set(new ReactiveTile());
        //         break;
        //     case TileType.Deadly:
        //         entity.Set(new InstantDeath());
        //         break;
        //     case TileType.Objective:
        //         entity.Set(new Objective());
        //         break;
        // }
        return entity;
    }
}

public enum TileType
{
    Default,
    Reactive,
    Deadly,
    Objective
}
