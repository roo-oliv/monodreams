using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Component.Draw;

/// <summary>
/// Unified drawing component that supports multiple rendering methods:
/// sprites, text, nine-patch, and mesh (vertex buffer) rendering.
/// </summary>
public class DrawComponent
{
    public DrawElementType Type;
    public RenderTargetID Target;

    // TransformComponent fields for all types
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
    public bool FlipHorizontally; // mirrors the source rect left-to-right (see SpriteInfoComponent)
    public bool FlipVertically; // mirrors the source rect top-to-bottom (see SpriteInfoComponent)

    // Text specific
    public BitmapFont? Font;
    public string? Text;
    public bool Underline; // When true, MasterRenderSystem strokes a thin underline (in Color) under each text line (see the text render path).
    public float LineSpacing = DynamicTextComponent.DefaultLineSpacing; // Multiplier on Font.LineHeight when laying out '\n'-separated lines (see MasterRenderSystem).

    // NinePatch specific
    public NinePatchInfo? NinePatchData;

    // Mesh specific (for vertex buffer rendering)
    public VertexPositionColor[]? Vertices;
    public int[]? Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;
    public Matrix? WorldMatrix; // TransformComponent matrix for mesh rendering

    // 16-bit index cache for Reach-safe mesh rendering — see Get16BitIndices().
    private int[]? _indices16Source;
    private short[]? _indices16;

    /// <summary>
    /// The mesh <see cref="Indices"/> as 16-bit values, for rendering through the
    /// <c>short[]</c> overload of <c>DrawUserIndexedPrimitives</c>. Returns <c>null</c> when there
    /// are no indices, or when the mesh has more vertices than a 16-bit index can address
    /// (more than 65536) — in which case the caller must fall back to the 32-bit
    /// <see cref="Indices"/> (valid only on the HiDef profile).
    /// <para>
    /// Why: the overload's index-array type selects the GPU index width (<c>int[]</c> ⇒ 32-bit,
    /// <c>short[]</c> ⇒ 16-bit), and the Reach profile (WebGL ES2 / BlazorGL) rejects 32-bit
    /// indices with <c>"Reach profile does not support 32 bit indices"</c>. Procedural meshes are
    /// tiny (far below the 16-bit ceiling), so rendering them through 16-bit indices works on
    /// every profile with no profile branch — the mesh analog of the sprite-run flush
    /// (see <see cref="MonoDreams.System.Draw.SpriteBatchFlush"/>).
    /// </para>
    /// The converted array is cached and rebuilt only when <see cref="Indices"/> is reassigned
    /// (reference identity changes), so a static mesh converts once — the per-frame render loop
    /// allocates nothing and the heap stays flat.
    /// </summary>
    public short[]? Get16BitIndices()
    {
        if (Indices is not { Length: > 0 }) return null;
        // A 16-bit index addresses vertices 0..65535, so up to 65536 vertices fit.
        if (Vertices is { Length: > ushort.MaxValue + 1 }) return null;

        if (!ReferenceEquals(_indices16Source, Indices))
        {
            // Reuse the buffer when the new index array is the same length (common for an
            // animated mesh that rewrites its indices in place each frame).
            if (_indices16 is not { } buffer || buffer.Length != Indices.Length)
                buffer = new short[Indices.Length];
            for (var i = 0; i < Indices.Length; i++)
                buffer[i] = (short)Indices[i]; // values ≤ 65535 round-trip as ushort bit patterns
            _indices16 = buffer;
            _indices16Source = Indices;
        }
        return _indices16;
    }

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
