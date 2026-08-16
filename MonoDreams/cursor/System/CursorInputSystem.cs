using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component.Cursor;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System.Cursor;

/// <summary>
/// Reads the hardware mouse into <see cref="CursorInputComponent"/>. <c>ScreenPosition</c> is in
/// <b>backbuffer pixels</b>: the raw OS mouse position (window points) multiplied by the viewport
/// manager's <c>DevicePixelRatio</c> — 1 on every ordinary run (byte-identical to reading the
/// mouse raw), &gt;1 when the host enabled a device-resolution backbuffer behind a scaled window
/// (macOS Retina under the editor run flag; see the level-editor module's <c>EditorHiDpi</c>).
/// This keeps the single-space invariant every consumer depends on: chrome hit-tests
/// (<c>ToolbarSystem</c>/<c>SystemsPanelSystem</c> bounds) and the letterbox inverse mapping
/// (<c>ViewportManager.MapMouse</c>) both operate in backbuffer pixels, so
/// <c>ScreenPosition</c> must be too. A null <paramref name="viewportManager"/> (the pre-DPR
/// signature) behaves as ratio 1.
/// </summary>
public class CursorInputSystem(World world, ViewportManager viewportManager = null)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorControllerComponent>().With<CursorInputComponent>().AsSet())
{
    /// <summary>
    /// When <c>true</c>, the system does <b>not</b> read <see cref="Mouse"/> and does not overwrite any
    /// injected <see cref="CursorInputComponent"/> field (screen/virtual/world position, delta, the
    /// button + press/release edges, scroll). The injected state survives the input pass untouched.
    /// Mirrors <c>AKeyboardInputHandlingSystem.SkipHardwareRead</c>: an editor-op / replay channel sets
    /// it so a scripted cursor drives selection / gizmo / toolbar with no real mouse. Default
    /// <c>false</c> → normal hardware behaviour, so every existing screen is byte-identical (back-compat).
    /// (Injected positions are authored directly in backbuffer pixels — the device-pixel ratio never
    /// applies to them.)
    /// </summary>
    public bool SkipHardwareRead { get; set; }

    /// <summary>
    /// Test/replay seam overriding the hardware mouse read. Default <c>null</c> → <see cref="Mouse"/>.
    /// A headless test sets it to drive a scripted press→release through the REAL edge derivation
    /// (the path <see cref="SkipHardwareRead"/> deliberately bypasses), exercising the interaction
    /// with a downstream consumer that clears the cursor's button state.
    /// </summary>
    public Func<MouseState> MouseStateProvider { get; set; }

    protected override void Update(GameState state, in Entity entity)
    {
        // Skip the hardware read entirely so injected cursor state survives (editor-op / replay channel).
        if (SkipHardwareRead) return;

        ref var input = ref entity.Get<CursorInputComponent>();
        var mouseState = MouseStateProvider != null ? MouseStateProvider() : Mouse.GetState();

        // Store previous positions
        input.PreviousScreenPosition = input.ScreenPosition;
        input.PreviousWorldPosition = input.WorldPosition;

        // Update current screen position in BACKBUFFER pixels (world position is calculated
        // later, after camera updates). See the class doc for the DevicePixelRatio contract.
        input.ScreenPosition = mouseState.Position.ToVector2() * (viewportManager?.DevicePixelRatio ?? 1f);

        // Calculate delta
        input.Delta = input.ScreenPosition - input.PreviousScreenPosition;

        // Derive the press/release edges from the previous HARDWARE levels this system owns
        // (PreviousLeftButton/etc.), NOT from the current LeftButton/etc. level fields. A downstream
        // consumer — e.g. an open modal dialog's pointer-edge consume — clears those level fields to
        // suppress the click; reusing them as the "previous" state let that clear poison the next
        // frame's edges (LeftButton forced false ⇒ LeftButtonReleased could never fire ⇒ a modal that
        // acts on release never saw its own click). The previous-fields are written from the raw
        // read below, before any consumer runs, so the edges are immune to the consume.
        var prevLeft = input.PreviousLeftButton;
        var prevRight = input.PreviousRightButton;
        var prevMiddle = input.PreviousMiddleButton;

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

        // Record THIS frame's true hardware levels for next frame's edge derivation, straight from
        // the read above so a downstream consumer's clear of LeftButton/etc. never corrupts them.
        input.PreviousLeftButton = input.LeftButton;
        input.PreviousRightButton = input.RightButton;
        input.PreviousMiddleButton = input.MiddleButton;

        // Scroll wheel
        var prevScroll = input.ScrollWheelValue;
        input.ScrollWheelValue = mouseState.ScrollWheelValue;
        input.ScrollWheelDelta = input.ScrollWheelValue - prevScroll;

        entity.NotifyChanged<CursorInputComponent>();
    }
}
