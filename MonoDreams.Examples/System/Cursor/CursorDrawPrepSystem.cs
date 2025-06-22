using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.System.Cursor;

// TODO: Render target is hardcoded here, Size too, LayerDepth is hardcoded, Opacity is hardcoded, these should all be configurable
public class CursorDrawPrepSystem(World world) 
    : AEntitySetSystem<GameState>(world.GetEntities()
        .With<CursorController>()
        .With<CursorTextures>()
        .With<DrawComponent>()
        .With<Position>()
        .AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var controller = ref entity.Get<CursorController>();
        ref var textures = ref entity.Get<CursorTextures>();
        ref var position = ref entity.Get<Position>();
        var drawComponent = entity.Get<DrawComponent>();
        
        // Clear previous draw elements
        drawComponent.Drawables.Clear();
        
        // Only add draw element if cursor is visible
        if (!controller.IsVisible || !textures.Textures.ContainsKey(controller.Type))
            return;
        
        var texture = textures.Textures[controller.Type];
        
        drawComponent.Drawables.Add(new DrawElement
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
            Texture = texture,
            Position = position.Current,
            Size = new Vector2(32),
            LayerDepth = 1.0f, // Highest layer depth for cursor
            Color = Color.White,
        });
    }
}