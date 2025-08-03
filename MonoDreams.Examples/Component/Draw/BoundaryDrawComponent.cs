using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Examples.Component.Draw;

// Specialized DrawComponent for level boundaries visualization
public class BoundaryDrawComponent
{
    public DrawElementType Type = DrawElementType.Triangles;
    public RenderTargetID Target = RenderTargetID.Main;
    public Vector2 Position;
    public float Rotation;
    public Vector2 Origin;
    public Color Color = Color.White;
    public float LayerDepth = 0.1f; // Low layer depth to render below the track
    public VertexPositionColor[] Vertices;
    public int[] Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;

    // Additional properties for boundary visualization
    public bool IsTrackInsideBoundary = true;
}
