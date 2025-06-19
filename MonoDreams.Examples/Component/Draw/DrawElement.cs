using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Examples.Component.Draw;

public struct DrawElement
{
    public DrawElementType Type;
    public RenderTargetID Target; // Which RT this belongs to

    // Common fields - Add others like Rotation, Origin, Scale, Effects if needed often
    public Vector2 Position;
    public Color Color;
    public float LayerDepth; // Crucial for sorting

    // Sprite specific / Also used by NinePatch for texture source
    public Texture2D Texture;
    public Rectangle? SourceRectangle;
    public Vector2 Size; // Target size on screen

    // Text specific
    public SpriteFont Font; // Or your glyph texture/info if using bitmap font
    public string Text;    // The string to draw (SpriteFont ignores SourceRectangle/Texture)
    // For bitmap fonts, you might store pre-calculated glyph data instead.

    // NinePatch specific (Texture/SourceRectangle reference the spritesheet)
    public NinePatchInfo? NinePatchData; // Your existing 9-patch definition struct/class
    // Note: For 9-patch, Position typically refers to the top-left corner, and Size is the overall target dimensions.
    // The MasterRenderSystem will need logic to handle rendering the 9 slices based on this.
    // Alternatively, SpritePrepSystem could break it into 9 separate DrawElements of Type = Sprite. (Let's do this for simplicity in MasterRenderSystem)
}