using Flecs.NET.Core;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Examples.Component.Cursor;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Cursor;

public static class CursorInputSystem
{
    public static void Register(World world, MonoDreams.Component.Camera camera)
    {
        world.System<CursorController, CursorInput>()
            .Kind(InputPhase)
            .Each((ref CursorController controller, ref CursorInput input) =>
            {
                var mouseState = Mouse.GetState();

                // Store previous positions
                input.PreviousScreenPosition = input.ScreenPosition;
                input.PreviousWorldPosition = input.WorldPosition;

                // Update current positions
                input.ScreenPosition = mouseState.Position.ToVector2();
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
            });
    }
}
