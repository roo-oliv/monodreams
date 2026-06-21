using System;
using System.ComponentModel;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component.Draw;

public struct DynamicTextComponent : IComponent
{
    /// <summary>
    /// Engine-wide default leading (multiplier on the font's line height) applied to multi-line
    /// ('\n'-separated) text when a component leaves <see cref="LineSpacing"/> at its default (≤ 0).
    /// The renderer (and any hand-rolled vertical stacking, e.g. DialogueSystem's options) must use
    /// this same value so on-screen line advances stay in sync. Single-line text is unaffected.
    /// </summary>
    public const float DefaultLineSpacing = 1.15f;

    public RenderTargetID Target; // Which RenderTarget this belongs to
    public float LayerDepth; // Your existing layer enum/struct
    public string TextContent; // The full text to potentially display
    public BitmapFont Font; // BitmapFont from MonoGame.Extended
    public Color Color;
    public float Scale; // Scale factor for the font size
    public bool Underline; // When true, MasterRenderSystem draws a thin underline (in Color) under each rendered line. Default false = no underline. See the text render path.
    public float LineSpacing; // Multiplier on the font's line height for multi-line ('\n') text; <= 0 means DefaultLineSpacing. See MasterRenderSystem's text path.
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
