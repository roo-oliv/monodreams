using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
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
        var entity = world.CreateEntity();

        entity.Set(new CursorController(initialType));
        entity.Set(new CursorInput());
        entity.Set(new Transform(Vector2.Zero));
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = renderTarget,
            Texture = cursorTextures[initialType],
            SourceRectangle = new Rectangle(0, 0, cursorTextures[initialType].Width, cursorTextures[initialType].Height),
            Color = Color.White,
            Position = Vector2.Zero,
            Size = new Vector2(64),
            LayerDepth = 1.0f, // Highest layer for cursor
        });
        entity.Set(new CursorTextures { Textures = cursorTextures });
        
        return entity;
    }
}