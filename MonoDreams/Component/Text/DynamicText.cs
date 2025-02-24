using System;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component;

public struct DynamicText
{
    public string Value;
    public BitmapFont Font;
    public float RevealingSpeed;
    public readonly Enum DrawLayer;
    public float RevealStartTime;
    public bool IsRevealed = false;
    public readonly RenderTarget2D RenderTarget;

    public DynamicText(RenderTarget2D renderTarget, string value, BitmapFont font,
        float revealingSpeed = 20, Enum drawLayer = null) : this()
    {
        Font = font;
        Value = value;
        RevealingSpeed = revealingSpeed;
        DrawLayer = drawLayer;
        RevealStartTime = float.NaN;
        RenderTarget = renderTarget;
        
    }
}
