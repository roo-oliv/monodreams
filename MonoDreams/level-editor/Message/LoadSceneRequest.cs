#nullable enable
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.Message;

/// <summary>
/// Requests loading a <b>native MonoDreams scene</b> (the editor's own format) — either from a path
/// (a file / content read) or from an <b>already-built <see cref="SceneData"/> in memory</b>.
///
/// <para>This message is deliberately <b>separate</b> from <c>LoadLevelRequest</c>. Exactly one
/// dispatcher handles <c>LoadLevelRequest</c> per pipeline — the native-only
/// <c>LevelLoadRequestSystem</c> at game boot, or a format module's own loader in an import
/// pipeline (<c>level-ldtk</c>'s <c>LDtkLevelLoadSystem</c>, which loads an <c>.ldtk</c> file and
/// sets the level components that drive its tile + entity parsers). Sharing that message would
/// mean a native-scene load also entered whichever level dispatcher the screen composed — and, on
/// a miss, let it clobber the world-level level state. A dedicated message keeps the load paths
/// independent and lets the reader be published DIRECTLY, with no dispatcher composed at all:
/// <c>LoadSceneRequest</c> drives only the <c>SceneReaderSystem</c>, which reconstructs entities
/// from serialized components.</para>
///
/// <para><b>In-memory restore (UX2-F).</b> The <see cref="Scene"/> overload carries an already-built
/// <see cref="SceneData"/> so a restore skips the file read but runs the <b>identical</b> reader
/// pipeline (re-tag roots, texture rehydration incl. <c>file:</c> keys, <c>DrawComponent</c> restore,
/// view framing, ensure-one-camera). This is the ONE in-memory entry point the Game-mode sandbox
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

    /// <summary>
    /// Suppresses the reader's <b>ensure-one-camera</b> step on this load (PF-D, pre-mortem #8): a
    /// <b>prefab context</b> has no camera entity (a prefab is a class — the writer/expander refuse a
    /// camera inside one), so its content-load must NOT create a default camera. When set, the reader
    /// still auto-frames the free VIEW on the loaded content (Edit-only, Play-disabled) but never ensures
    /// a camera. Only ever true on the in-memory prefab-context path (open / tab-switch restore); a
    /// scene / Game-tab load leaves it false so the scene always has exactly one camera entity.
    /// </summary>
    public readonly bool SuppressCameraEnsure;

    /// <param name="path">Path to the scene JSON (see <see cref="Path"/>).</param>
    /// <param name="fromContent"><c>true</c> to resolve as a content asset (works on web via
    /// <c>TitleContainer</c>); <c>false</c> for a host-filesystem read. Defaults to <c>true</c> so a
    /// shipped scene loads on every backend.</param>
    public LoadSceneRequest(string path, bool fromContent = true)
    {
        Path = path;
        FromContent = fromContent;
        Scene = null;
        SuppressCameraEnsure = false;
    }

    /// <summary>The in-memory restore overload (UX2-F): reconstruct from an already-built
    /// <paramref name="scene"/> through the same reader pipeline, no file I/O.
    /// <paramref name="suppressCameraEnsure"/> (PF-D) is set for a prefab-context load so the reader never
    /// creates a default camera (a prefab has none — pre-mortem #8).</summary>
    public LoadSceneRequest(SceneData scene, bool suppressCameraEnsure = false)
    {
        Path = "<in-memory>";
        FromContent = false;
        Scene = scene;
        SuppressCameraEnsure = suppressCameraEnsure;
    }
}
