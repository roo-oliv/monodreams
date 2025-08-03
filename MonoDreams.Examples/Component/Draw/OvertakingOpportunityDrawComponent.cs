using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Examples.Component.Draw;

// Specialized DrawComponent for overtaking opportunity visualization
public class OvertakingOpportunityDrawComponent
{
    public DrawElementType Type = DrawElementType.Triangles;
    public RenderTargetID Target = RenderTargetID.Main;
    public Vector2 Position;
    public float Rotation;
    public Vector2 Origin;
    public Color Color = new Color(203, 30, 75); // Bright red color
    public float LayerDepth = 0.95f;
    public VertexPositionColor[] Vertices;
    public int[] Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;
}
