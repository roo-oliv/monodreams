using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "Mesh indices render through 16-bit indices (Reach-safe)":
/// <see cref="DrawComponent.Get16BitIndices"/> converts the authoring <c>int[]</c> mesh indices
/// to a <c>short[]</c> so <c>MasterRenderSystem.DrawSingleMesh</c> renders meshes through the
/// 16-bit <c>DrawUserIndexedPrimitives</c> overload, which the Reach profile (WebGL / BlazorGL)
/// accepts. The 32-bit <c>int[]</c> overload throws "Reach profile does not support 32 bit
/// indices" on Reach — the crash that made the player's orb mesh take down the whole web frame.
/// The conversion is cached (rebuilt only on reassignment) so the per-frame render loop allocates
/// nothing, and falls back to <c>null</c> (caller uses the 32-bit overload, HiDef-only) for a mesh
/// too large for a 16-bit index.
/// </summary>
public class MeshIndexConversionTests
{
    private static DrawComponent Mesh(int[] indices, int vertexCount) => new()
    {
        Type = DrawElementType.Mesh,
        Indices = indices,
        Vertices = new VertexPositionColor[vertexCount],
    };

    /// The short[] must hold exactly the int[] index values (the common procedural-mesh case).
    [Fact]
    public void Convert_ProducesShortIndicesMatchingTheIntValues()
    {
        var dc = Mesh([0, 1, 2, 0, 2, 3], vertexCount: 4);

        var shorts = dc.Get16BitIndices();

        Assert.NotNull(shorts);
        Assert.Equal(6, shorts!.Length);
        for (var i = 0; i < shorts.Length; i++)
            Assert.Equal(dc.Indices![i], (int)shorts[i]);
    }

    /// A static mesh converts once: repeated calls return the SAME array, so the per-frame render
    /// loop allocates nothing (protects the heap-flat invariant the headless demo tests assert).
    [Fact]
    public void Convert_IsCached_AcrossRepeatedCalls()
    {
        var dc = Mesh([0, 1, 2], vertexCount: 3);

        var first = dc.Get16BitIndices();
        var second = dc.Get16BitIndices();

        Assert.Same(first, second);
    }

    /// Reassigning Indices (a generator rewrote the mesh) invalidates the cache by reference
    /// identity, so the next call reflects the NEW values — the player factory sets Indices
    /// directly, never through SetMeshData, so the cache must key off the array, not the setter.
    [Fact]
    public void Convert_RebuildsWhenIndicesReassigned()
    {
        var dc = Mesh([0, 1, 2], vertexCount: 4);
        _ = dc.Get16BitIndices();

        // Same-length reassignment: contents update; the buffer is reused in place (an intended
        // allocation-free optimization for a mesh that rewrites its indices each frame).
        dc.Indices = [2, 1, 0];
        var sameLength = dc.Get16BitIndices();
        Assert.Equal([2, 1, 0], sameLength);

        // Different-length reassignment: a fresh buffer with the new contents.
        dc.Indices = [3, 2, 1, 0];
        var diffLength = dc.Get16BitIndices();
        Assert.Equal([3, 2, 1, 0], diffLength);
        Assert.NotSame(sameLength, diffLength);
    }

    /// No indices => null (the renderer skips the mesh; HasValidMesh already gates this).
    [Fact]
    public void Convert_ReturnsNull_WhenNoIndices()
    {
        Assert.Null(new DrawComponent { Type = DrawElementType.Mesh }.Get16BitIndices());
        Assert.Null(Mesh([], vertexCount: 0).Get16BitIndices());
    }

    /// Index values above 32767 must round-trip as unsigned 16-bit bit patterns: the GPU reads the
    /// short[] as ushort, so (short)value for value in [0, 65535] carries the correct 16 bits.
    [Fact]
    public void Convert_RoundTripsValuesAbove32767_AsUnsigned16Bit()
    {
        var dc = Mesh([0, 40000, 65535], vertexCount: 65536);

        var shorts = dc.Get16BitIndices();

        Assert.NotNull(shorts);
        Assert.Equal(0, (int)(ushort)shorts![0]);
        Assert.Equal(40000, (int)(ushort)shorts[1]);
        Assert.Equal(65535, (int)(ushort)shorts[2]);
    }

    /// A mesh with more vertices than a 16-bit index can address (> 65536) returns null, so the
    /// renderer falls back to the 32-bit int[] overload (HiDef-only) instead of silently
    /// truncating an index. Procedural generators never reach this, but the guard must hold.
    [Fact]
    public void Convert_ReturnsNull_WhenVerticesExceed16BitCeiling()
    {
        // 65537 vertices => an index could be 65536, which a 16-bit index cannot hold.
        var dc = Mesh([0, 1, 2], vertexCount: 65537);

        Assert.Null(dc.Get16BitIndices());
    }
}
