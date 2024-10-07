using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.Examples.Objects;

public static class Player
{
    public static Entity Create(World world, Texture2D texture, Vector2 position, Enum? drawLayer = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new PlayerState());
        entity.Set(new InputControlled());
        entity.Set(new Position(position));
        entity.Set(new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize)));
        entity.Set(new DrawInfo(texture, Constants.PlayerSize, layer: drawLayer));
        entity.Set(new RigidBody());
        entity.Set(new Velocity());
        return entity;
    }
}
