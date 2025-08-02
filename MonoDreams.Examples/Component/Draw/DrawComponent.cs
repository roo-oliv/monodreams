using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Examples.Component.Draw;

public class DrawComponent
{
    public DrawElementType Type;
    public RenderTargetID Target; // Which RT this belongs to

    // Common fields - Add others like Rotation, Origin, Scale, Effects if needed often
    public Vector2 Position;
    public float Rotation; // In radians, 0 = right, positive = counter-clockwise
    public Vector2 Origin;
    public Color Color;
    public float LayerDepth;

    // Sprite specific / Also used by NinePatch for texture source
    public Texture2D Texture;
    public Rectangle? SourceRectangle;
    public Vector2 Size; // Target size on screen

    // Text specific
    public SpriteFont Font; // Or your glyph texture/info if using bitmap font
    public string Text;    // The string to draw (SpriteFont ignores SourceRectangle/Texture)

    // NinePatch specific (Texture/SourceRectangle reference the spritesheet)
    public NinePatchInfo? NinePatchData; // Your existing 9-patch definition struct/class

    // Triangle mesh specific
    public VertexPositionColor[] Vertices;
    public int[] Indices;
    public PrimitiveType PrimitiveType;
}