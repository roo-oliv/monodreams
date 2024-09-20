using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.LegacyComponents;
using MonoDreams.State;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.System;

public class CursorSystem(World world, Camera camera)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorController>().With<PlayerInput>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        ref var playerInput = ref entity.Get<PlayerInput>();
        
        // var cameraCursorPosition = camera.ScreenToWorld(playerInput.CursorPosition);
        
        // position.NextLocation = cameraCursorPosition;
        entity.NotifyChanged<Position>();
    }
}