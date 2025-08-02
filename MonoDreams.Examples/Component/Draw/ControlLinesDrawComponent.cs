using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Examples.Component.Draw;

// Specialized DrawComponent for lines connecting control points
public class ControlLinesDrawComponent
{
    public DrawElementType Type = DrawElementType.Triangles;
    public RenderTargetID Target = RenderTargetID.Main;
    public Vector2 Position;
    public float Rotation;
    public Vector2 Origin;
    public Color Color = Color.White;
    public float LayerDepth = 0.85f;
    public VertexPositionColor[] Vertices;
    public int[] Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;
}
