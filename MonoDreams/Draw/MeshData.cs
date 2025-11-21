using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Draw;

/// <summary>
/// Holds vertex buffer data for rendering triangle meshes.
/// This is game-agnostic and can be used for any procedural geometry.
/// </summary>
public struct MeshData
{
    public VertexPositionColor[] Vertices;
    public int[] Indices;
    public PrimitiveType PrimitiveType;
    public bool IsDirty;

    public MeshData()
    {
        Vertices = [];
        Indices = [];
        PrimitiveType = PrimitiveType.TriangleList;
        IsDirty = true;
    }

    public MeshData(VertexPositionColor[] vertices, int[] indices, PrimitiveType primitiveType = PrimitiveType.TriangleList)
    {
        Vertices = vertices;
        Indices = indices;
        PrimitiveType = primitiveType;
        IsDirty = true;
    }

    public readonly bool IsValid => Vertices is { Length: > 0 } && Indices is { Length: > 0 };

    public readonly int PrimitiveCount => PrimitiveType switch
    {
        PrimitiveType.TriangleList => Indices.Length / 3,
        PrimitiveType.TriangleStrip => Indices.Length - 2,
        PrimitiveType.LineList => Indices.Length / 2,
        PrimitiveType.LineStrip => Indices.Length - 1,
        _ => 0
    };
}

/// <summary>
/// Defines the rendering method to use for a drawable entity.
/// </summary>
public enum RenderMethod
{
    /// <summary>Standard SpriteBatch rendering for textures and sprites.</summary>
    Sprite,
    /// <summary>Text rendering using SpriteFont.</summary>
    Text,
    /// <summary>Nine-patch/nine-slice rendering for scalable UI elements.</summary>
    NinePatch,
    /// <summary>Vertex buffer rendering using BasicEffect for procedural geometry.</summary>
    Mesh
}
