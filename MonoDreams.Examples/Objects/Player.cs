using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics.VelocityBased;
using Position = MonoDreams.Component.Physics.Position;

namespace MonoDreams.Examples.Objects;

public static class Player
{
    public static Entity Create(World world, int gravity, Texture2D texture, Vector2 position, Enum? drawLayer = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new InputControlled());
        entity.Set(new Position(position));
        entity.Set(new BoxCollidable(new Rectangle(Point.Zero, Constants.PlayerSize)));
        entity.Set(new DrawInfo(texture, Constants.PlayerSize, layer: drawLayer));
        entity.Set(new Gravity(gravity));
        entity.Set(new Velocity());
        return entity;
    }
}
