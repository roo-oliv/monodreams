using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component.Cursor;
using MonoDreams.State;

namespace MonoDreams.System.Cursor;

public class CursorInputSystem(World world)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorControllerComponent>().With<CursorInputComponent>().AsSet())
{
    /// <summary>
    /// When <c>true</c>, the system does <b>not</b> read <see cref="Mouse"/> and does not overwrite any
    /// injected <see cref="CursorInputComponent"/> field (screen/virtual/world position, delta, the
    /// button + press/release edges, scroll). The injected state survives the input pass untouched.
    /// Mirrors <c>AKeyboardInputHandlingSystem.SkipHardwareRead</c>: an editor-op / replay channel sets
    /// it so a scripted cursor drives selection / gizmo / toolbar with no real mouse. Default
    /// <c>false</c> → normal hardware behaviour, so every existing screen is byte-identical (back-compat).
    /// </summary>
    public bool SkipHardwareRead { get; set; }

    protected override void Update(GameState state, in Entity entity)
    {
        // Skip the hardware read entirely so injected cursor state survives (editor-op / replay channel).
        if (SkipHardwareRead) return;

        ref var input = ref entity.Get<CursorInputComponent>();
        var mouseState = Mouse.GetState();

        // Store previous positions
        input.PreviousScreenPosition = input.ScreenPosition;
        input.PreviousWorldPosition = input.WorldPosition;

        // Update current screen position (world position is calculated later after camera updates)
        input.ScreenPosition = mouseState.Position.ToVector2();
        
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
        
        entity.NotifyChanged<CursorInputComponent>();
    }
}
