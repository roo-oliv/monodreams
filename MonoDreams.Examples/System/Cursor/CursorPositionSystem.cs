using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.State;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.System.Cursor;

public class CursorPositionSystem(World world) 
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorController>().With<CursorInput>().With<Position>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        ref var input = ref entity.Get<CursorInput>();
        ref var controller = ref entity.Get<CursorController>();
        
        // Update cursor position based on input
        position.Current = input.WorldPosition + controller.HotSpot;
        
        entity.NotifyChanged<Position>();
    }
}