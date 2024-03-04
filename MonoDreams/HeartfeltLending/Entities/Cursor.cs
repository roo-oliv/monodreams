using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.InputState;

namespace HeartfeltLending.Entities;

public static class Cursor
{
    public static Entity Create(World world, Texture2D texture)
    {
        var entity = world.CreateEntity();
        entity.Set(new CursorController());
        entity.Set(new PlayerInput());
        entity.Set(new Position(Vector2.Zero));
        entity.Set(new Collidable(new Rectangle(0, 0, 1, 1), passive: false));
        entity.Set(new DrawInfo(texture));
        return entity;
    }
}