using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LegacyComponents;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.Objects.UI;

// TODO: Revise the way these factories work and where they belong in the project structure.
public static class Cursor
{
    public static Entity Create(RenderTarget2D renderTarget, World world, Texture2D texture, Point size, Enum drawLayer = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new CursorController());
        entity.Set(new PlayerInput());
        entity.Set(new Position(Vector2.Zero));
        entity.Set(new BoxCollider(new Rectangle(0, 0, 1, 1), passive: false));
        entity.Set(new DrawInfo(renderTarget, texture, size, layer: drawLayer));
        return entity;
    }
}
