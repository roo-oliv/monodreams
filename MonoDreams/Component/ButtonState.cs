using Microsoft.Xna.Framework;
using NotImplementedException = System.NotImplementedException;

namespace MonoDreams.Component;

public struct ButtonState
{
    public Color DefaultColor { get; }
    public Color SelectedColor { get; }
    public bool Selected { get; private set; }
    public bool Pressed { get; private set; }

    public ButtonState(Color defaultColor, Color selectedColor)
    {
        DefaultColor = defaultColor;
        SelectedColor = selectedColor;
        Selected = false;
        Pressed = false;
    }

    public void Select() => Selected = true;
    
    public void Press() => Pressed = true;
    
    public void Reset()
    {
        Selected = false;
        Pressed = false;
    }
}