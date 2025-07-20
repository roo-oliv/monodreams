﻿using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(SpriteInfo), typeof(Position))]
public class CullingSystem(World world, MonoDreams.Component.Camera camera) : AEntitySetSystem<GameState>(world)
{
    public bool IsEnabled { get; set; } = true;

    protected override void Update(GameState state, in Entity entity)
    {
        var position = entity.Get<Position>();
        var spriteInfo = entity.Get<SpriteInfo>();
        
        // Calculate entity bounds in world space
        var entityBounds = new Rectangle(
            (int)(position.Current.X + spriteInfo.Offset.X),
            (int)(position.Current.Y + spriteInfo.Offset.Y),
            (int)spriteInfo.Size.X,
            (int)spriteInfo.Size.Y
        );
        
        // Check if entity is visible
        var isVisible = camera.VirtualScreenBounds.Intersects(entityBounds);
        
        // Add or remove the Visible component based on culling result
        if (isVisible)
        {
            if (!entity.Has<Visible>())
            {
                entity.Set<Visible>();
            }
        }
        else
        {
            if (entity.Has<Visible>())
            {
                entity.Remove<Visible>();
            }
        }
    }
    
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
