using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.LegacyComponents;
using MonoDreams.State;

namespace MonoDreams.System;

public class CursorSystem(World world, Camera camera)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorController>().With<PlayerInput>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<Transform>();
        ref var playerInput = ref entity.Get<PlayerInput>();

        // var cameraCursorPosition = camera.ScreenToWorld(playerInput.CursorPosition);

        // transform.Position = cameraCursorPosition;
        entity.NotifyChanged<Transform>();
    }
}