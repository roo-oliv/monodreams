using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Examples.Component.Draw;

public struct TriangleMeshInfo()
{
    public VertexPositionColor[] Vertices = [];
    public int[] Indices = [];
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;
    public RenderTargetID Target = RenderTargetID.Main;
    public float LayerDepth = 0f;
}
