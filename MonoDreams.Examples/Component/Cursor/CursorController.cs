using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component.Cursor;

public struct CursorController
{
    public CursorType Type { get; set; }
    public bool IsVisible { get; set; }
    public Vector2 HotSpot { get; set; }
    
    public CursorController(CursorType type = CursorType.Default, bool isVisible = true, Vector2 hotSpot = default)
    {
        Type = type;
        IsVisible = isVisible;
        HotSpot = hotSpot;
    }
}

public enum CursorType
{
    Default,
    Pointer,
    Hand,
    Text,
    Crosshair,
    Resize,
    Wait
}