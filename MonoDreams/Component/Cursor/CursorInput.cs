using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Cursor;

public struct CursorInput
{
    public Vector2 ScreenPosition { get; set; }
    public Vector2 WorldPosition { get; set; }
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
    public int ScrollWheelValue { get; set; }
    public int ScrollWheelDelta { get; set; }
}
