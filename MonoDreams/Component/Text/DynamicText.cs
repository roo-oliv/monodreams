using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component;

public struct DynamicText
{
    public string Value;
    public BitmapFont Font;
    public float RevealingSpeed;
    public float RevealStartTime;
    public bool IsRevealed = false;

    public DynamicText(string value, BitmapFont font, float revealingSpeed = 20) : this()
    {
        Font = font;
        Value = value;
        RevealingSpeed = revealingSpeed;
        RevealStartTime = float.NaN;
    }
}
