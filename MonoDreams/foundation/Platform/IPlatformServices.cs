using System.IO;

namespace MonoDreams.Platform;

/// <summary>
/// The portability seam between MonoDreams engine source and the host OS / backend.
/// Engine modules never touch <c>System.IO.File</c>, <c>System.IO.Directory</c>,
/// <c>System.AppDomain</c>, <c>System.Environment</c>, or <c>System.Console</c>
/// directly; they go through <see cref="PlatformServices.Current"/> instead. The
/// head project (desktop / web) selects the concrete implementation at startup.
///
/// The desktop implementation (<see cref="DesktopPlatformServices"/>) maps every
/// member to the real filesystem / process environment, reproducing the engine's
/// historical behaviour exactly. A web (Blazor/WASM) head supplies its own
/// implementation — Console/in-memory/browser-storage backed, no-op or blob-download
/// for outputs — without recompiling any engine module.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// The base directory the process resolves relative paths against — the desktop
    /// equivalent of <c>AppDomain.CurrentDomain.BaseDirectory</c> (the executable's
    /// output folder). Used to locate <c>debug/</c>, <c>Content/</c>, and settings
    /// files. Always ends with a directory separator on desktop.
    /// </summary>
    string BaseDirectory { get; }

    /// <summary>
    /// Looks up an environment variable, or returns <c>null</c> when unset. On web
    /// there is no process environment, so an implementation may return host-config
    /// values (e.g. <c>MONODREAMS_DEBUG_DIR</c>) or <c>null</c> for everything.
    /// </summary>
    string GetEnvironmentVariable(string name);

    /// <summary>Combines path segments using the host's path conventions.</summary>
    string CombinePath(params string[] paths);

    /// <summary>Returns whether a readable file exists at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>Reads the full text of a file. Caller handles exceptions.</summary>
    string ReadAllText(string path);

    /// <summary>Writes text to a file, overwriting any existing content.</summary>
    void WriteAllText(string path, string contents);

    /// <summary>Writes bytes to a file, overwriting any existing content.</summary>
    void WriteAllBytes(string path, byte[] bytes);

    /// <summary>
    /// Persists an editor-exported scene (or similar user-authored output) so the user can keep it,
    /// using whatever delivery the host supports. Unlike <see cref="WriteAllText"/> — a plain
    /// host-filesystem write — this is the <i>output-to-the-user</i> seam: the desktop head writes a
    /// file under <see cref="BaseDirectory"/> and returns its path; a web head triggers a browser
    /// download / clipboard copy (or, until that is wired, logs a warning and returns the contents so
    /// the caller can surface them). The returned string is a host-meaningful locator (a file path on
    /// desktop) or <c>null</c> when the export was delivered out-of-band (e.g. a browser download).
    /// </summary>
    /// <param name="suggestedFileName">A file name the host may use (e.g. <c>"scene.json"</c>).</param>
    /// <param name="contents">The text to export.</param>
    /// <returns>A host-meaningful locator (desktop file path), or <c>null</c> if delivered out-of-band.</returns>
    string ExportScene(string suggestedFileName, string contents);

    /// <summary>Creates a directory (and any missing parents); a no-op if it exists.</summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Opens the log sink for the <see cref="MonoDreams.State.Logger"/>: a
    /// <see cref="TextWriter"/> the logger writes each line to. The desktop sink is a
    /// file <see cref="StreamWriter"/> under <paramref name="directory"/>; a web sink
    /// may return an in-memory or Console-backed writer. The returned writer is owned
    /// by the caller, which disposes it on <c>Logger.Shutdown</c>.
    /// </summary>
    TextWriter OpenLogWriter(string directory, string fileName);

    /// <summary>
    /// Echoes a fully-formatted log line to the host's console / developer output.
    /// Desktop routes to <c>System.Console</c>; web routes to the browser console.
    /// </summary>
    void WriteLineToConsole(string line);

    /// <summary>
    /// Runs <paramref name="work"/> off the main thread when the host supports it
    /// (desktop), or inline when it does not (single-threaded WASM). Used by
    /// best-effort, fire-and-forget I/O such as periodic screenshot saving.
    /// </summary>
    void RunBackground(global::System.Action work);
}
