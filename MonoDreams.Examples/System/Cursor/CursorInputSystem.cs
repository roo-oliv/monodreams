using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Renderer;
using MonoDreams.State;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.System.Cursor;

public class CursorInputSystem(World world, MonoDreams.Component.Camera camera, ViewportManager viewportManager) 
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorController>().With<CursorInput>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var input = ref entity.Get<CursorInput>();
        var mouseState = Mouse.GetState();
        
        // Store previous positions
        input.PreviousScreenPosition = input.ScreenPosition;
        input.PreviousWorldPosition = input.WorldPosition;
        
        // Get raw mouse position and convert to virtual coordinates
        var rawMousePosition = mouseState.Position.ToVector2();
        var virtualMousePosition = viewportManager.ScaleMouseToVirtualCoordinates(rawMousePosition);
        
        // Update current positions using properly scaled coordinates
        input.ScreenPosition = virtualMousePosition ?? rawMousePosition; // Fallback to raw if scaling fails
        input.WorldPosition = camera.VirtualScreenToWorld(input.ScreenPosition);
        
        // Calculate delta
        input.Delta = input.ScreenPosition - input.PreviousScreenPosition;
        
        // Update button states
        var prevLeft = input.LeftButton;
        var prevRight = input.RightButton;
        var prevMiddle = input.MiddleButton;
        
        input.LeftButton = mouseState.LeftButton == ButtonState.Pressed;
        input.RightButton = mouseState.RightButton == ButtonState.Pressed;
        input.MiddleButton = mouseState.MiddleButton == ButtonState.Pressed;
        
        // Calculate press/release states
        input.LeftButtonPressed = input.LeftButton && !prevLeft;
        input.RightButtonPressed = input.RightButton && !prevRight;
        input.MiddleButtonPressed = input.MiddleButton && !prevMiddle;
        
        input.LeftButtonReleased = !input.LeftButton && prevLeft;
        input.RightButtonReleased = !input.RightButton && prevRight;
        input.MiddleButtonReleased = !input.MiddleButton && prevMiddle;
        
        // Scroll wheel
        var prevScroll = input.ScrollWheelValue;
        input.ScrollWheelValue = mouseState.ScrollWheelValue;
        input.ScrollWheelDelta = input.ScrollWheelValue - prevScroll;
        
        entity.NotifyChanged<CursorInput>();
    }
}