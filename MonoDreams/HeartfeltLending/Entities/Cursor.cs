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
        entity.Set(new Position(new Vector2(50, 50)));
        entity.Set(new DrawInfo(texture));
        return entity;
    }
}