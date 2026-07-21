using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Cursor;

public struct CursorInputComponent
{
    public Vector2 ScreenPosition { get; set; }
    /// Cursor position in the virtual-resolution coordinate system (after letterbox scaling,
    /// before the camera transform). Use this for hit-testing HUD-target elements so the
    /// camera moving does not desync the cursor from on-screen UI.
    public Vector2 VirtualPosition { get; set; }
    public Vector2 WorldPosition { get; set; }
    /// True while the OS pointer is outside the aspect-fit game viewport — in the letterbox bars
    /// or in the editor shell's chrome margins. Set by CursorPositionSystem each frame; while
    /// true, VirtualPosition/WorldPosition keep their last inside-the-viewport values, so systems
    /// acting on click/scroll edges in world space must ignore those edges (the pointer is over
    /// chrome, which hit-tests ScreenPosition instead). Defaults to false, so injected cursor
    /// state (replay/editor-op channels, tests) behaves as "inside" unless stated otherwise.
    public bool OutsideViewport { get; set; }
    public Vector2 PreviousScreenPosition { get; set; }
    public Vector2 PreviousWorldPosition { get; set; }
    public Vector2 Delta { get; set; }
    public bool LeftButton { get; set; }
    public bool RightButton { get; set; }
    public bool MiddleButton { get; set; }
    public bool LeftButtonPressed { get; set; }
    public bool RightButtonPressed { get; set; }
    public bool MiddleButtonPressed { get; set; }
    public bool LeftButtonReleased { get; set; }
    public bool RightButtonReleased { get; set; }
    public bool MiddleButtonReleased { get; set; }
    /// Last frame's hardware button levels, owned and written by CursorInputSystem straight from the
    /// mouse read (before any consumer runs). The press/release edges derive from THESE, not from the
    /// mutable LeftButton/etc. level fields — so a downstream consumer that clears the level state to
    /// suppress the click (a modal dialog's pointer-edge consume) does NOT poison the next frame's
    /// edge computation. Mirrors how ScrollWheelValue (the accumulator) survives a consumer clearing
    /// only ScrollWheelDelta (the derived edge). Not read on the SkipHardwareRead / injected path.
    public bool PreviousLeftButton { get; set; }
    public bool PreviousRightButton { get; set; }
    public bool PreviousMiddleButton { get; set; }
    public int ScrollWheelValue { get; set; }
    public int ScrollWheelDelta { get; set; }
}
