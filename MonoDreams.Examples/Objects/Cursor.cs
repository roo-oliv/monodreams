using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Component.Cursor;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;
using DefaultEcsWorld = DefaultEcs.World;
using DefaultEcsEntity = DefaultEcs.Entity;

namespace MonoDreams.Examples.Objects;

public static class Cursor
{
    public static DefaultEcsEntity Create(
        World world,
        DefaultEcsWorld defaultEcsWorld,
        Dictionary<CursorType, Texture2D> cursorTextures,
        RenderTargetID renderTarget,
        CursorType initialType = CursorType.Default)
    {
        var cursorControllerComponent = new CursorController(initialType);
        var cursorInputComponent = new CursorInput();
        var positionComponent = new Position(Vector2.Zero);
        var drawComponent = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = renderTarget,
            Texture = cursorTextures[initialType],
            Color = Color.White,
            Position = Vector2.Zero,
            Size = new Vector2(128, 128),
            LayerDepth = 1.0f, // Highest layer for cursor
        };
        var cursorTexturesComponent = new CursorTextures { Textures = cursorTextures };

        // Create Flecs entity
        world.Entity()
            .Set(cursorControllerComponent)
            .Set(cursorInputComponent)
            .Set(positionComponent)
            .Set(drawComponent)
            .Set(cursorTexturesComponent);

        // Create DefaultECS entity
        var entity = defaultEcsWorld.CreateEntity();
        entity.Set(cursorControllerComponent);
        entity.Set(cursorInputComponent);
        entity.Set(positionComponent);
        entity.Set(drawComponent);
        entity.Set(cursorTexturesComponent);

        return entity;
    }
}