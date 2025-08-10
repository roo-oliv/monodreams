using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.State;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.System.Cursor;

public class CursorPositionSystem(World world) 
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorController>().With<CursorInput>().With<Transform>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Transform>();
        ref var input = ref entity.Get<CursorInput>();
        ref var controller = ref entity.Get<CursorController>();
        
        // Use screen position directly for cursor rendering to avoid camera influence
        // The cursor should always appear at the mouse position regardless of camera movement
        position.CurrentPosition = input.ScreenPosition + controller.HotSpot;
        
        entity.NotifyChanged<Transform>();
    }
}