using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Component.Dialogue;

/// <summary>
/// Holds the state required to render the Dialogue UI.
/// Typically added to a single entity representing the UI.
/// </summary>
public struct DialogueUIStateComponent : IComponent
{
    // --- Control ---
    public bool IsActive; // Is the dialogue UI currently visible?

    // --- Content ---
    public Texture2D SpeakerEmoteTexture; // Texture for the speaker's portrait/emote. Null if none.
    public string CurrentText;         // The current line of text to display.
    public BitmapFont DialogueFont;      // Font to use for the text.
    public Color TextColor;            // Color of the text.

    // --- Indicators ---
    public bool ShowNextIndicator; // Show the "continue" prompt graphic?
    public Texture2D NextIndicatorTexture; // Texture for the indicator.

    // --- Layout & Appearance ---
    public Texture2D DialogueBoxTexture; // The 9-patch texture for the background box.
    public NinePatchInfo DialogueBoxNinePatch; // Definition of the 9-patch slicing for the box.
    public Vector2 DialogueBoxSize; // The target size for the dialogue box on screen.

    public Vector2 DialogueBoxOffset;  // Position offset for the dialogue box relative to the main UI top-left.
    public Vector2 EmoteOffset;        // Position offset for the emote relative to the main UI top-left.
    public Vector2 TextOffset;         // Position offset for the text block relative to the main UI top-left.
    public Rectangle TextArea;         // Bounding box for text placement relative to the main UI top-left (used for positioning/wrapping).
    public Vector2 NextIndicatorOffset; // Position offset for the indicator relative to the main UI top-left.

    // Defaults can be set here or when creating the component
    // Example: public Color TextColor = Color.White;
    public void Dispose()
    {
        SpeakerEmoteTexture?.Dispose();
        NextIndicatorTexture?.Dispose();
        DialogueBoxTexture?.Dispose();
        GC.SuppressFinalize(this);
    }

    public ISite? Site { get; set; }
    public bool IsTextFullyRevealed { get; set; }
    public int TextRevealingSpeed { get; set; }
    public int VisibleCharacterCount { get; set; }
    public float TextRevealStartTime { get; set; }

    public event EventHandler? Disposed;
}