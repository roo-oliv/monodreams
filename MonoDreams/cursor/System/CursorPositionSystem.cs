using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System.Cursor;

public class CursorPositionSystem(World world, MonoDreams.Component.Camera camera, ViewportManager viewportManager)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorControllerComponent>().With<CursorInputComponent>().With<TransformComponent>().With<DrawComponent>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<TransformComponent>();
        ref var input = ref entity.Get<CursorInputComponent>();
        ref var controller = ref entity.Get<CursorControllerComponent>();
        ref var draw = ref entity.Get<DrawComponent>();

        // First convert screen position to virtual coordinates
        var virtualPosition = viewportManager.ScaleMouseToVirtualCoordinates(input.ScreenPosition);

        if (virtualPosition.HasValue)
        {
            // Always calculate world position for systems that need it (e.g., ButtonInteractionSystem)
            input.VirtualPosition = virtualPosition.Value;
            input.WorldPosition = camera.VirtualScreenToWorld(virtualPosition.Value);

            // Set transform.Position based on render target:
            // - HUD: use virtual coords (no camera transform for rendering)
            // - Main: use world coords (camera transform applied during rendering)
            if (draw.Target == RenderTargetID.HUD)
            {
                transform.Position = virtualPosition.Value + controller.HotSpot;
            }
            else
            {
                transform.Position = input.WorldPosition + controller.HotSpot;
            }
        }
        // else: Mouse is outside the viewport (in letterbox/pillarbox area) - keep previous position
        // Console.WriteLine($"Mouse: {transform.Position}");

        entity.NotifyChanged<CursorInputComponent>();
        entity.NotifyChanged<TransformComponent>();
    }
}
