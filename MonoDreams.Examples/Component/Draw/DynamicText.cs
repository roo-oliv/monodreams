using System.ComponentModel;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Component.Draw;

public struct DynamicText : IComponent
{
    public RenderTargetID Target; // Which RenderTarget this belongs to
    public float LayerDepth; // Your existing layer enum/struct
    public string TextContent; // The full text to potentially display
    public BitmapFont Font; // BitmapFont from MonoGame.Extended
    public Color Color;
    public float Scale; // Scale factor for the font size
    public float RevealingSpeed; // Characters per second
    public float RevealStartTime; // GameTime total seconds when reveal started
    public bool IsRevealed;      // Flag if reveal is complete
    public int VisibleCharacterCount; // How many characters are currently visible
    // Add alignment, origin, scale, effects etc. if needed
    // Store calculated glyphs here if TextUpdateSystem prepares them
    // public List<GlyphInfo> CalculatedGlyphs;
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public ISite? Site { get; set; }
    public event EventHandler? Disposed;
}