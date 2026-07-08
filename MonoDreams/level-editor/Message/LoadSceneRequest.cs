#nullable enable
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.Message;

/// <summary>
/// Requests loading a <b>native MonoDreams scene</b> (the editor's own format) — either from a path
/// (a file / content read) or from an <b>already-built <see cref="SceneData"/> in memory</b>.
///
/// <para>This message is deliberately <b>separate</b> from <c>LoadLevelRequest</c>. The LDtk
/// handler on <c>LoadLevelRequest</c> (<c>LevelLoadRequestSystem</c>) unconditionally runs
/// <c>content.Load&lt;LDtkLevel&gt;</c> and sets / removes the <c>CurrentLevelComponent</c> singleton —
/// driving the LDtk tile + entity parsers. If the native scene loader shared that message, a
/// native-scene load would also trigger (and on failure, clobber) the LDtk pipeline. A dedicated
/// message keeps the two load paths independent: <c>LoadSceneRequest</c> drives only the
/// <c>SceneReaderSystem</c>, which reconstructs entities from serialized components — never via the
/// LDtk content path.</para>
///
/// <para><b>In-memory restore (UX2-F).</b> The <see cref="Scene"/> overload carries an already-built
/// <see cref="SceneData"/> so a restore skips the file read but runs the <b>identical</b> reader
/// pipeline (re-tag roots, texture rehydration incl. <c>file:</c> keys, <c>DrawComponent</c> restore,
/// camera-rig re-sync + view framing). This is the ONE in-memory entry point the Game-mode sandbox
/// exit reuses — so the reader stays the single restore implementation (pre-mortem #2), and every load
/// still flows through this ONE message + subscriber (the message-driven premise holds).</para>
/// </summary>
public readonly struct LoadSceneRequest
{
    /// <summary>Path to the scene JSON file — read through the content stream (<c>TitleContainer</c>)
    /// when <see cref="FromContent"/>, else through <c>IPlatformServices</c>. A synthetic
    /// <c>"&lt;in-memory&gt;"</c> tag on the <see cref="Scene"/> overload (used only for logging).</summary>
    public readonly string Path;

    /// <summary>Whether to resolve <see cref="Path"/> as a content asset (vs. host-filesystem user data).
    /// Ignored on the in-memory overload (<see cref="Scene"/> is non-null).</summary>
    public readonly bool FromContent;

    /// <summary>The already-built scene to restore (UX2-F in-memory entry point). When non-null the
    /// reader uses it directly and performs NO file read; null = read from <see cref="Path"/>.</summary>
    public readonly SceneData? Scene;

    /// <param name="path">Path to the scene JSON (see <see cref="Path"/>).</param>
    /// <param name="fromContent"><c>true</c> to resolve as a content asset (works on web via
    /// <c>TitleContainer</c>); <c>false</c> for a host-filesystem read. Defaults to <c>true</c> so a
    /// shipped scene loads on every backend.</param>
    public LoadSceneRequest(string path, bool fromContent = true)
    {
        Path = path;
        FromContent = fromContent;
        Scene = null;
    }

    /// <summary>The in-memory restore overload (UX2-F): reconstruct from an already-built
    /// <paramref name="scene"/> through the same reader pipeline, no file I/O.</summary>
    public LoadSceneRequest(SceneData scene)
    {
        Path = "<in-memory>";
        FromContent = false;
        Scene = scene;
    }
}
