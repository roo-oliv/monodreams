using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Component.Cursor;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.Objects;

public static class Cursor
{
    public static Entity Create(
        World world,
        Dictionary<CursorType, Texture2D> cursorTextures,
        RenderTargetID renderTarget,
        CursorType initialType = CursorType.Default)
    {
        var texture = cursorTextures[initialType];
        var size = new Vector2(128, 128);

        var cursorControllerComponent = new CursorController(initialType);
        var cursorInputComponent = new CursorInput();
        var positionComponent = new Position(Vector2.Zero);
        var spriteInfoComponent = new SpriteInfo
        {
            SpriteSheet = texture,
            Source = new Rectangle(0, 0, texture.Width, texture.Height),
            Size = size,
            Color = Color.White,
            Target = renderTarget,
            LayerDepth = 1.0f, // Highest layer for cursor
            Offset = Vector2.Zero
        };
        var drawComponent = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = renderTarget,
            Texture = texture,
            Color = Color.White,
            Position = Vector2.Zero,
            Size = size,
            LayerDepth = 1.0f,
        };
        var cursorTexturesComponent = new CursorTextures { Textures = cursorTextures };

        // Create Flecs entity
        var entity = world.Entity()
            .Set(cursorControllerComponent)
            .Set(cursorInputComponent)
            .Set(positionComponent)
            .Set(spriteInfoComponent)
            .Set(drawComponent)
            .Set(cursorTexturesComponent);

        return entity;
    }
}