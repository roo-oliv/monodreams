﻿using DefaultEcs;
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
    private readonly Vector2 _size = new(32);
    
    protected override void Update(GameState state, in Entity entity)
    {
        ref var controller = ref entity.Get<CursorController>();
        ref var textures = ref entity.Get<CursorTextures>();
        ref var position = ref entity.Get<Position>();
        ref var drawComponent = ref entity.Get<DrawComponent>();
        
        // Only add draw element if cursor is visible
        if (!controller.IsVisible || !textures.Textures.TryGetValue(controller.Type, out var value))
            return;

        drawComponent.Texture = value;
        drawComponent.Position = position.Current;
        drawComponent.Size = _size;
    }
}