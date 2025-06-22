using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using System.Collections.Generic;
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
        
        entity.Set(new CursorController(initialType, true));
        entity.Set(new CursorInput());
        entity.Set(new Position(Vector2.Zero));
        entity.Set(new DrawComponent()); // Using Examples' DrawComponent
        entity.Set(new CursorTextures { Textures = cursorTextures });
        
        // Initialize the draw component with cursor sprite
        var drawComponent = entity.Get<DrawComponent>();
        var initialTexture = cursorTextures[initialType];
        
        drawComponent.Drawables.Add(new DrawElement
        {
            Type = DrawElementType.Sprite,
            Target = renderTarget,
            Texture = initialTexture,
            Color = Color.White,
            Position = Vector2.Zero,
            Size = new Vector2(128, 128),
            LayerDepth = 1.0f, // Highest layer for cursor
        });
        
        return entity;
    }
}