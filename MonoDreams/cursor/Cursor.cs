using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Cursor;

namespace MonoDreams.Cursor;

public static class Cursor
{
    public static Entity Create(
        World world,
        Dictionary<CursorType, Texture2D> cursorTextures,
        RenderTargetID renderTarget,
        CursorType initialType = CursorType.Default)
    {
        var entity = world.CreateEntity();

        entity.Set(new CursorControllerComponent(initialType));
        entity.Set(new CursorInputComponent());
        entity.Set(new TransformComponent(Vector2.Zero));
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = renderTarget,
            Texture = cursorTextures[initialType],
            SourceRectangle = new Rectangle(0, 0, cursorTextures[initialType].Width, cursorTextures[initialType].Height),
            Color = Color.White,
            Position = Vector2.Zero,
            Size = new Vector2(16),
            LayerDepth = 1.0f, // Highest layer for cursor
        });
        entity.Set(new CursorTexturesComponent { Textures = cursorTextures });
        
        return entity;
    }
}