using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Level;

namespace MonoDreams.Examples.Objects;

public static class Zone
{
    public static Entity Create(ILevel level, Vector2 position, Point size, Action<ZoneState> callback)
    {
        var entity = level.World.CreateEntity();
        entity.Set(new EntityInfo(EntityType.Zone));
        entity.Set(new Transform(position));
        entity.Set(new BoxCollider(new Rectangle(Point.Zero, size), passive: true));
        entity.Set(new ZoneState(callback));
        return entity;
    }
}