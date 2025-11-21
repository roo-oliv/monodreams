using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;

namespace MonoDreams.Examples.Component.Draw;

/// <summary>
/// Unified drawing component that supports multiple rendering methods:
/// sprites, text, nine-patch, and mesh (vertex buffer) rendering.
/// </summary>
public class DrawComponent
{
    public DrawElementType Type;
    public RenderTargetID Target;

    // Transform fields for all types
    public Vector2 Position;
    public float Rotation;
    public Vector2 Origin;
    public Vector2 Scale = Vector2.One;
    public Color Color = Color.White;
    public float LayerDepth;

    // Sprite specific
    public Texture2D? Texture;
    public Rectangle? SourceRectangle;
    public Vector2 Size;

    // Text specific
    public SpriteFont? Font;
    public string? Text;

    // NinePatch specific
    public NinePatchInfo? NinePatchData;

    // Mesh specific (for vertex buffer rendering)
    public VertexPositionColor[]? Vertices;
    public int[]? Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;

    /// <summary>
    /// Sets the mesh data from a MeshData struct.
    /// </summary>
    public void SetMeshData(MeshData meshData)
    {
        Type = DrawElementType.Mesh;
        Vertices = meshData.Vertices;
        Indices = meshData.Indices;
        PrimitiveType = meshData.PrimitiveType;
    }

    /// <summary>
    /// Sets the mesh data from a mesh generator.
    /// </summary>
    public void SetMeshData(IMeshGenerator generator)
    {
        SetMeshData(generator.Generate());
    }

    /// <summary>
    /// Gets the primitive count for mesh rendering.
    /// </summary>
    public int GetPrimitiveCount()
    {
        if (Indices == null || Indices.Length == 0) return 0;

        return PrimitiveType switch
        {
            PrimitiveType.TriangleList => Indices.Length / 3,
            PrimitiveType.TriangleStrip => Indices.Length - 2,
            PrimitiveType.LineList => Indices.Length / 2,
            PrimitiveType.LineStrip => Indices.Length - 1,
            _ => 0
        };
    }

    public bool HasValidMesh => Vertices is { Length: > 0 } && Indices is { Length: > 0 };
}