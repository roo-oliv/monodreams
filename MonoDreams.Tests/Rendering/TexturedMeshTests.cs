using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "A mesh may be textured (`TexturedVertices` + `Texture`)" and the
/// extended half of "Mesh indices render through 16-bit indices (Reach-safe)": a mesh may carry
/// <see cref="DrawComponent.TexturedVertices"/> instead of <see cref="DrawComponent.Vertices"/> — one
/// draw sampling a sheet, which is how a whole chunk of tiles becomes ONE culled/sorted/drawn thing —
/// and the 16-bit vertex ceiling that keeps a mesh renderable on the Reach profile now covers BOTH
/// buffers.
/// <para>
/// These are the pure, GraphicsDevice-free halves. The "a textured quad renders with correct UVs and
/// PointClamp" proof needs a real GPU and lives in the headless Demos run
/// (<c>MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs</c> +
/// <c>TexturedMeshUVCheckSystem</c> in the physics demo). The loud-guard half lives in
/// <see cref="TexturedMeshIndexCeilingWarningTests"/>, which has to serialize against the
/// process-global <see cref="Logger"/>.
/// </para>
/// </summary>
public class TexturedMeshTests
{
    private static DrawComponent TexturedMesh(int[]? indices, int vertexCount) => new()
    {
        Type = DrawElementType.Mesh,
        Indices = indices,
        TexturedVertices = new VertexPositionColorTexture[vertexCount],
    };

    // ─── the 16-bit ceiling now covers the TEXTURED buffer too ────────────────────────────────────

    /// A textured mesh past the ceiling (> 65536 vertices, so an index could be 65536) returns null,
    /// exactly like the colour buffer does — the renderer then falls back to the 32-bit int[] overload
    /// instead of silently truncating an index. Before this, the check read only `Vertices`, so an
    /// oversized TEXTURED chunk sailed past it and got 16-bit indices with wrapped values.
    [Fact]
    public void Convert_ReturnsNull_WhenTexturedVerticesExceed16BitCeiling()
    {
        var dc = TexturedMesh([0, 1, 2], vertexCount: 65537);

        Assert.Null(dc.Get16BitIndices());
    }

    /// ...and exactly AT the ceiling it must still convert: 65536 vertices are addressed by indices
    /// 0..65535, which is precisely what 16 bits hold. An off-by-one here would push every
    /// maximum-size chunk onto the HiDef-only path (blank on web) for no reason.
    [Fact]
    public void Convert_Succeeds_AtExactly65536TexturedVertices()
    {
        var dc = TexturedMesh([0, 1, 65535], vertexCount: 65536);

        var shorts = dc.Get16BitIndices();

        Assert.NotNull(shorts);
        Assert.Equal(65535, (int)(ushort)shorts![2]);
    }

    // ─── HasValidMesh / MeshVertexCount / IsTexturedMesh ──────────────────────────────────────────

    /// HasValidMesh accepts EITHER vertex buffer — the sort filter in MasterRenderSystem gates the
    /// mesh path on it, so a textured mesh that it rejected would never be drawn at all.
    [Fact]
    public void HasValidMesh_AcceptsTexturedBufferWithIndices_RejectsWithout()
    {
        Assert.True(TexturedMesh([0, 1, 2], vertexCount: 3).HasValidMesh);

        // Textured vertices but no indices: nothing to draw (Get16BitIndices also returns null).
        var noIndices = TexturedMesh(null, vertexCount: 3);
        Assert.False(noIndices.HasValidMesh);
        Assert.Null(noIndices.Get16BitIndices());

        // Indices but neither vertex buffer: still nothing to draw.
        Assert.False(new DrawComponent { Type = DrawElementType.Mesh, Indices = [0, 1, 2] }.HasValidMesh);
    }

    /// MeshVertexCount reports whichever buffer is set — the textured one first (the two are mutually
    /// exclusive by contract), then the colour one, then zero.
    [Fact]
    public void MeshVertexCount_ReportsWhicheverBufferIsSet()
    {
        Assert.Equal(4, TexturedMesh([0, 1, 2], vertexCount: 4).MeshVertexCount);

        Assert.Equal(3, new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = new VertexPositionColor[3],
            Indices = [0, 1, 2],
        }.MeshVertexCount);

        Assert.Equal(0, new DrawComponent { Type = DrawElementType.Mesh }.MeshVertexCount);
    }

    /// IsTexturedMesh needs BOTH halves. With no Texture the mesh is not textured — and that
    /// combination is exactly the NRE hazard the renderer guards: it passes HasValidMesh, is not
    /// textured, and the colour branch has no Vertices to read, so MasterRenderSystem skips it (and
    /// warns) instead of dereferencing null.
    ///
    /// The TRUE case cannot be asserted here: constructing a Texture2D needs a GraphicsDevice, which a
    /// unit test has no way to create. It is covered by the headless physics-demo integration test,
    /// where a real textured quad renders and the run reads its pixels back.
    [Fact]
    public void IsTexturedMesh_IsFalse_WithoutATexture()
    {
        var dc = TexturedMesh([0, 1, 2], vertexCount: 4);

        Assert.False(dc.IsTexturedMesh);
        Assert.True(dc.HasValidMesh); // ...which is why the renderer needs its own guard
        Assert.Null(dc.Vertices);
    }

    /// A colour-only mesh must be untouched by all of the above: not textured, counted from the colour
    /// buffer, and still converting its indices. (The full byte-identical contract for the colour path
    /// is MeshIndexConversionTests, which passes unmodified.)
    [Fact]
    public void ColourOnlyMesh_IsUnaffected()
    {
        var dc = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = new VertexPositionColor[4],
            Indices = [0, 1, 2, 2, 1, 3],
        };

        Assert.False(dc.IsTexturedMesh);
        Assert.True(dc.HasValidMesh);
        Assert.Equal(4, dc.MeshVertexCount);
        Assert.Equal(2, dc.GetPrimitiveCount());
        Assert.Equal(6, dc.Get16BitIndices()!.Length);
    }
}

/// <summary>
/// The LOUD half of the 16-bit ceiling: crossing it must produce a <see cref="Logger"/> warning that
/// names the platform cliff — desktop HiDef renders the 32-bit fallback fine, Reach (web / BlazorGL)
/// renders NOTHING with no exception — and it must produce it EXACTLY ONCE per oversized buffer,
/// because the check sits on the per-frame render path. Silence here is how the *Witch v Necromancer*
/// class of bug happens: a level that works on the dev machine and is a blank canvas in the browser,
/// with only a code comment to explain why the chunks were kept small.
/// <para>
/// <see cref="Logger"/> and <see cref="PlatformServices.Current"/> are process-global, so this class
/// joins the existing non-parallel collection (shared with <c>LoggerInterpolationTests</c> and
/// <c>PlatformServicesTests</c>) and observes the sinks through a fake platform rather than the disk.
/// </para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class TexturedMeshIndexCeilingWarningTests
{
    /// <summary>Minimal in-memory <see cref="IPlatformServices"/> — enough to capture the two Logger
    /// sinks. Mirrors the fakes in <c>LoggerInterpolationTests</c> / <c>PlatformServicesTests</c>.</summary>
    private sealed class FakePlatformServices : IPlatformServices
    {
        public string BaseDirectory => "/fake/base/";
        public List<string> ConsoleLines { get; } = new();
        public StringWriter LogWriter { get; } = new();

        // IPlatformServices is nullable-oblivious (the engine does not enable NRT); `null!` keeps this
        // nullable-enabled test assembly quiet without changing the contract.
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
        public void RunBackground(Action work) => work();
    }

    private static List<string> RunCapturingLog(Action body)
    {
        var fake = new FakePlatformServices();
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            Logger.Shutdown();                 // close whatever an earlier test left open
            Logger.Initialize("logdir");       // ...and reopen on the fake sink at Debug
            body();
            Logger.Shutdown();                 // flush
        }
        finally
        {
            Logger.Shutdown();
            PlatformServices.Current = previous; // restore the desktop default
        }

        return fake.ConsoleLines
            .Where(l => !l.Contains("Logger initialized.") && !l.Contains("Logger shutting down."))
            .ToList();
    }

    /// Crossing the ceiling warns — loudly (WARN level, naming the count and the web failure mode) —
    /// and warns only ONCE no matter how many frames call Get16BitIndices() on the same mesh.
    [Fact]
    public void CeilingFallback_WarnsLoudly_ExactlyOncePerBuffer()
    {
        var dc = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = new VertexPositionColor[65537],
            Indices = [0, 1, 2],
        };

        var lines = RunCapturingLog(() =>
        {
            // Three "frames" over the same mesh.
            Assert.Null(dc.Get16BitIndices());
            Assert.Null(dc.Get16BitIndices());
            Assert.Null(dc.Get16BitIndices());
        });

        var warnings = lines.Where(l => l.Contains("16-bit index ceiling")).ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains("[ WARN]", warning);
        Assert.Contains("65537 vertices", warning);
        // The whole point of the line: it must state the platform consequence, not just the fallback.
        Assert.Contains("Reach", warning);
        Assert.Contains("renders NOTHING", warning);
    }

    /// The textured buffer gets the same loud treatment (the guard covers both buffers), and a NEW
    /// oversized buffer warns again — the de-duplication keys off array reference identity, so it
    /// silences repeat frames, never a genuinely different mesh.
    [Fact]
    public void CeilingFallback_WarnsForTheTexturedBuffer_AndAgainForANewBuffer()
    {
        var dc = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            TexturedVertices = new VertexPositionColorTexture[70000],
            Indices = [0, 1, 2],
        };

        var lines = RunCapturingLog(() =>
        {
            Assert.Null(dc.Get16BitIndices());
            Assert.Null(dc.Get16BitIndices());

            // A different oversized buffer is a different mesh: it must be reported too.
            dc.TexturedVertices = new VertexPositionColorTexture[80000];
            Assert.Null(dc.Get16BitIndices());
            Assert.Null(dc.Get16BitIndices());
        });

        var warnings = lines.Where(l => l.Contains("16-bit index ceiling")).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains("70000 vertices", warnings[0]);
        Assert.Contains("80000 vertices", warnings[1]);
    }

    /// A textured mesh with no Texture is skipped by the renderer, which reports it through this
    /// once-per-buffer seam rather than throwing an NRE inside the draw loop.
    [Fact]
    public void TexturedMeshWithoutTexture_WarnsOncePerBuffer()
    {
        var dc = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            TexturedVertices = new VertexPositionColorTexture[4],
            Indices = [0, 1, 2, 2, 1, 3],
        };

        var lines = RunCapturingLog(() =>
        {
            dc.WarnTexturedMeshWithoutTexture();
            dc.WarnTexturedMeshWithoutTexture();
        });

        var warning = Assert.Single(lines, l => l.Contains("TexturedVertices but no Texture"));
        Assert.Contains("[ WARN]", warning);
    }

    /// A mesh WITHIN the ceiling must stay silent — the guard is a cliff warning, not a mesh audit.
    [Fact]
    public void MeshWithinTheCeiling_LogsNothing()
    {
        var dc = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            TexturedVertices = new VertexPositionColorTexture[4],
            Indices = [0, 1, 2, 2, 1, 3],
        };

        var lines = RunCapturingLog(() => Assert.NotNull(dc.Get16BitIndices()));

        Assert.DoesNotContain(lines, l => l.Contains("16-bit index ceiling"));
    }
}
