using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;
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

    /// <summary>
    /// TEXTURED mesh vertices — set these (with <see cref="Texture"/>) instead of
    /// <see cref="Vertices"/> to draw one mesh sampling a sheet, which is how a whole grid of tiles
    /// becomes ONE draw instead of one entity-and-quad per cell. <see cref="Indices"/>,
    /// <see cref="PrimitiveType"/> and <see cref="WorldMatrix"/> work identically for both.
    /// Mutually exclusive with <see cref="Vertices"/>; <see cref="HasValidMesh"/> accepts either.
    /// <para>
    /// Rendered through the same <c>BasicEffect</c> as a vertex-coloured mesh, with
    /// <c>TextureEnabled</c> and the sprite sampler (<c>PointClamp</c> by default — pixel art must
    /// not bilinear-smear across cell edges). Per-vertex distortions a <c>SpriteBatch</c> quad
    /// cannot express (skew/shear, trapezoids, wave warps) belong here too. See the rendering
    /// premise "A mesh may be textured (`TexturedVertices` + `Texture`)".
    /// </para>
    /// </summary>
    public VertexPositionColorTexture[]? TexturedVertices;

    public int[]? Indices;
    public PrimitiveType PrimitiveType = PrimitiveType.TriangleList;
    public Matrix? WorldMatrix; // TransformComponent matrix for mesh rendering

    // 16-bit index cache for Reach-safe mesh rendering — see Get16BitIndices().
    private int[]? _indices16Source;
    private short[]? _indices16;

    // The vertex buffers the two LOUD mesh guards have already reported. Keyed off ARRAY REFERENCE
    // identity (exactly like _indices16Source) because both guards sit on the per-frame render path:
    // a warning must fire once per distinct buffer, not once per frame, and the check itself must
    // allocate nothing.
    private object? _ceilingWarnedFor;
    private object? _missingTextureWarnedFor;

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
    /// <para>
    /// The ceiling check covers BOTH vertex buffers (<see cref="Vertices"/> and
    /// <see cref="TexturedVertices"/>) and, when it trips, it trips <b>loudly</b> — see
    /// <see cref="WarnIndexCeilingExceeded"/>. Silence here is the worst possible outcome: the
    /// 32-bit fallback paints normally on desktop HiDef and renders NOTHING on Reach (web), with no
    /// exception to point at.
    /// </para>
    /// </summary>
    public short[]? Get16BitIndices()
    {
        if (Indices is not { Length: > 0 }) return null;
        // A 16-bit index addresses vertices 0..65535, so up to 65536 vertices fit. Past that the
        // caller has to use the HiDef-only 32-bit overload, which is a platform cliff — so say so.
        if (Vertices is { Length: > ushort.MaxValue + 1 })
        {
            WarnIndexCeilingExceeded(Vertices, Vertices.Length);
            return null;
        }
        if (TexturedVertices is { Length: > ushort.MaxValue + 1 })
        {
            WarnIndexCeilingExceeded(TexturedVertices, TexturedVertices.Length);
            return null;
        }

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
    /// The LOUD half of the 16-bit-index ceiling: one <see cref="Logger"/> warning per oversized
    /// vertex buffer, naming the vertex count and — the part nobody guesses — the platform
    /// consequence. Falling back to 32-bit indices is not a degradation, it is a profile cliff: the
    /// mesh paints normally on desktop (HiDef accepts 32-bit indices) and renders <b>nothing at all</b>
    /// on the Reach profile (WebGL ES2 / BlazorGL), with no exception thrown and no error logged —
    /// just a blank canvas in the browser and a working build on the developer's machine.
    /// <para>
    /// De-duplicated by vertex-buffer reference identity, so a mesh that sits past the ceiling for a
    /// whole session logs one line, not one per frame per mesh. (The flag is set even when the
    /// threshold discards the line, so the once-per-buffer contract does not depend on the log level.)
    /// </para>
    /// </summary>
    private void WarnIndexCeilingExceeded(object vertexBuffer, int vertexCount)
    {
        if (ReferenceEquals(_ceilingWarnedFor, vertexBuffer)) return;
        _ceilingWarnedFor = vertexBuffer;
        Logger.Warning(
            $"DrawComponent mesh exceeds the 16-bit index ceiling: {vertexCount} vertices (max 65536). " +
            "Falling back to 32-bit indices, which ONLY the HiDef profile accepts: on Reach " +
            "(web / BlazorGL) this mesh renders NOTHING — no exception, no error, a blank canvas. " +
            "Split the geometry into chunks of at most 65536 vertices (e.g. run-length-merge uniform " +
            "cells) to keep it on the 16-bit path.");
    }

    /// <summary>
    /// Reports (once per <see cref="TexturedVertices"/> buffer) a mesh that carries textured vertices
    /// but no <see cref="Texture"/> to sample. Such a mesh is not drawable — it is not
    /// <see cref="IsTexturedMesh"/>, and the vertex-coloured branch has no <see cref="Vertices"/> to
    /// read — so <c>MasterRenderSystem</c> skips it and calls this instead of dereferencing null.
    /// De-duplicated by buffer reference identity for the same reason the ceiling warning is.
    /// </summary>
    internal void WarnTexturedMeshWithoutTexture()
    {
        if (TexturedVertices == null || ReferenceEquals(_missingTextureWarnedFor, TexturedVertices)) return;
        _missingTextureWarnedFor = TexturedVertices;
        Logger.Warning(
            $"DrawComponent has {TexturedVertices.Length} TexturedVertices but no Texture — skipping " +
            "the mesh draw. Assign DrawComponent.Texture (the sheet the UVs address), or use " +
            "DrawComponent.Vertices for a vertex-coloured mesh.");
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

    /// <summary>Whether this mesh has geometry to draw: EITHER vertex buffer plus indices.</summary>
    public bool HasValidMesh =>
        (Vertices is { Length: > 0 } || TexturedVertices is { Length: > 0 }) && Indices is { Length: > 0 };

    /// <summary>Whether this mesh draws textured (<see cref="TexturedVertices"/> + a
    /// <see cref="Texture"/>) rather than vertex-coloured.</summary>
    public bool IsTexturedMesh => TexturedVertices is { Length: > 0 } && Texture != null;

    /// <summary>Vertex count of whichever mesh buffer is set.</summary>
    public int MeshVertexCount => TexturedVertices?.Length ?? Vertices?.Length ?? 0;
}
