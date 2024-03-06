using System;
using Microsoft.Xna.Framework;
using NotImplementedException = System.NotImplementedException;

namespace MonoDreams.Component;

public struct ButtonState
{
    public Color DefaultColor { get; }
    public Color SelectedColor { get; }
    public bool Selected { get; private set; }
    public bool Pressed { get; private set; }
    public Action Callback;

    public ButtonState(Color defaultColor, Color selectedColor, Action callback)
    {
        DefaultColor = defaultColor;
        SelectedColor = selectedColor;
        Selected = false;
        Pressed = false;
        Callback = callback;
    }

    public void Select() => Selected = true;
    
    public void Press()
    {
        Pressed = true;
        Callback.Invoke();
    }

    public void Release() => Pressed = false;
    
    public void Reset()
    {
        Selected = false;
        Pressed = false;
    }
}