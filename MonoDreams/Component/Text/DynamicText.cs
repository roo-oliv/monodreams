using System;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component;

public struct DynamicText(
    RenderTarget2D renderTarget,
    string value,
    BitmapFont font,
    float revealingSpeed = 20,
    Enum drawLayer = null,
    int maxLineWidth = 0)
{
    public string Value = value;
    public BitmapFont Font = font;
    public float RevealingSpeed = revealingSpeed;
    public readonly Enum DrawLayer = drawLayer;
    public float RevealStartTime = float.NaN;
    public bool IsRevealed = false;
    public readonly RenderTarget2D RenderTarget = renderTarget;
    public int MaxLineWidth = maxLineWidth;
}
