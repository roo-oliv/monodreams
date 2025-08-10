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
        .With<Transform>()
        .AsSet())
{
    private readonly Vector2 _size = new(32);
    
    protected override void Update(GameState state, in Entity entity)
    {
        ref var controller = ref entity.Get<CursorController>();
        ref var textures = ref entity.Get<CursorTextures>();
        ref var position = ref entity.Get<Transform>();
        ref var drawComponent = ref entity.Get<DrawComponent>();
        
        // Only add draw element if cursor is visible
        if (!controller.IsVisible || !textures.Textures.TryGetValue(controller.Type, out var value))
            return;

        drawComponent.Texture = value;
        drawComponent.Position = position.CurrentPosition;
        drawComponent.Size = _size;
        // Make sure cursor is drawn to a render target that doesn't use camera transform
        drawComponent.Target = RenderTargetID.UI; // Use UI render target instead of Main
    }
}