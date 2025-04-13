using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Component;

public class DialoguePresenter
{
    // Visual presentation settings
    public BitmapFont Font { get; set; }
    public Texture2D DialogueBoxTexture { get; set; }
    public Texture2D PortraitTexture { get; set; }
    
    // Layout settings
    public Rectangle DialogueBoxBounds { get; set; }
    public Rectangle PortraitBounds { get; set; }
    
    // Text display settings
    public Color TextColor { get; set; } = Color.White;
    public float TextSpeed { get; set; } = 1.0f;
    
    // Whether text should be revealed gradually
    public bool RevealTextGradually { get; set; } = true;
    
    // How much text has been revealed (0.0-1.0)
    public float TextRevealProgress { get; set; } = 0.0f;
}
