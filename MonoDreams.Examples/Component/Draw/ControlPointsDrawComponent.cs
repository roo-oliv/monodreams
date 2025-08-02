using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Examples.Component.Draw;

// Specialized DrawComponent for control points visualization
public class ControlPointsDrawComponent
{
    public DrawElementType Type = DrawElementType.Triangles;
    public RenderTargetID Target = RenderTargetID.Main;
    public Vector2 Position;
    public float Rotation;
    public Vector2 Origin;
    public Color Color = Color.White;
    public float LayerDepth = 0.9f;
    public VertexPositionColor[] Vertices;
    public int[] Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;
}
