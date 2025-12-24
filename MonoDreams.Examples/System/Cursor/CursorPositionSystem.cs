using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.State;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.System.Cursor;

public class CursorPositionSystem(World world, MonoDreams.Component.Camera camera)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorController>().With<CursorInput>().With<Transform>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<Transform>();
        ref var input = ref entity.Get<CursorInput>();
        ref var controller = ref entity.Get<CursorController>();

        // Calculate world position using current camera state (after camera has moved)
        input.WorldPosition = camera.VirtualScreenToWorld(input.ScreenPosition);

        // Update cursor position based on input
        transform.Position = input.WorldPosition + controller.HotSpot;

        entity.NotifyChanged<CursorInput>();
        entity.NotifyChanged<Transform>();
    }
}