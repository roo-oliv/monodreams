#nullable enable
using System;
using System.IO;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's <b>resolved project root + manifest</b>, computed once at editor init on desktop —
/// the bridge between the editor running from a build-output directory and the versioned project
/// source it authors into. It is the seam that later lets Save write into the source tree (PS3)
/// and here it makes the <see cref="GameProject"/> manifest available and gates Save on being
/// resolvable (see <see cref="EditorOverlay.SaveBlock"/>).
///
/// <para><b>Resolution order (banked decision 1)</b> — <see cref="Resolve(string,Func{string,string?},Func{string,bool},Func{string,string},int)"/>:</para>
/// <list type="number">
///   <item><b>PRIMARY — env var <see cref="ProjectRootVariable"/>.</b> Set in the dev run
///   configuration (the same one that carries <c>--editor</c>) to the project/content root; the
///   manifest is probed at <c>&lt;root&gt;/Content/game.mdproj</c> then <c>&lt;root&gt;/game.mdproj</c>.
///   This is the recommended way to target the <b>source</b> tree (so a later Save lands in git).</item>
///   <item><b>FALLBACK — walk up from <c>BaseDirectory</c>.</b> When the env var is unset (or names
///   a root with no manifest), ascend from the build-output dir probing <c>&lt;dir&gt;/game.mdproj</c>
///   and <c>&lt;dir&gt;/Content/game.mdproj</c> at each level (bounded depth, stop at the filesystem
///   root). This resolves when the editor runs from a normal build output inside the source tree, or
///   from the MGCB-copied <c>Content/</c> beside the executable.</item>
///   <item><b>UNRESOLVED — neither found</b> (a shipped/relocated build, a console, a malformed
///   manifest). <see cref="Resolved"/> is <c>false</c>; the editor never throws, and Save is disabled
///   with the "no project root" reason.</item>
/// </list>
///
/// <para><b>The project root is the directory that CONTAINS the manifest.</b> So
/// <see cref="ProjectRoot"/><c>/game.mdproj</c> == <see cref="ManifestPath"/> and
/// <see cref="ProjectRoot"/><c>/</c><see cref="LevelsDir"/> is the levels directory
/// (<see cref="LevelsPath"/>) — the invariant PS3's write path leans on, uniform across the env-var
/// and walk-up cases.</para>
///
/// <para><b>Game-agnostic + injectable.</b> The module never reads the environment or the filesystem
/// directly: the pure <see cref="Resolve(string,Func{string,string?},Func{string,bool},Func{string,string},int)"/>
/// takes the base directory + env/file lookups as delegates (so tests drive it with a simulated
/// layout, no real disk), and the no-arg <see cref="Resolve()"/> convenience wires them to
/// <see cref="PlatformServices.Current"/>. The desktop head resolves it where the editor flag is
/// parsed and hands it to the overlay.</para>
/// </summary>
public sealed class EditorProjectContext
{
    /// <summary>The environment variable naming the project/content root (primary resolution).</summary>
    public const string ProjectRootVariable = "MONODREAMS_PROJECT_ROOT";

    /// <summary>Default bound on how many ancestor directories the walk-up fallback probes.</summary>
    public const int DefaultMaxWalkUpDepth = 16;

    private EditorProjectContext(bool resolved, string? projectRoot, string? manifestPath,
        string levelsDir, GameProject? manifest)
    {
        Resolved = resolved;
        ProjectRoot = projectRoot;
        ManifestPath = manifestPath;
        LevelsDir = levelsDir;
        Manifest = manifest;
    }

    /// <summary>The shared "no project root" context (a shipped build / console / relocated output).
    /// <see cref="Resolved"/> is <c>false</c>; <see cref="LevelsDir"/> falls back to the default.</summary>
    public static readonly EditorProjectContext Unresolved =
        new(false, null, null, GameProject.DefaultLevelsDir, null);

    /// <summary>Whether a project root + parseable manifest were found. When <c>false</c>, Save is
    /// disabled with the "no project root" reason and the write path never runs.</summary>
    public bool Resolved { get; }

    /// <summary>The directory that contains the resolved manifest (the project root), or <c>null</c>
    /// when unresolved. PS3's write path combines this with <see cref="LevelsDir"/>.</summary>
    public string? ProjectRoot { get; }

    /// <summary>The full path to the resolved <c>game.mdproj</c>, or <c>null</c> when unresolved.</summary>
    public string? ManifestPath { get; }

    /// <summary>The levels directory relative to <see cref="ProjectRoot"/> (the manifest's
    /// <see cref="GameProject.LevelsDir"/>, or <see cref="GameProject.DefaultLevelsDir"/>).</summary>
    public string LevelsDir { get; }

    /// <summary>The resolved manifest, or <c>null</c> when unresolved.</summary>
    public GameProject? Manifest { get; }

    /// <summary>The absolute levels directory (<see cref="ProjectRoot"/> + <see cref="LevelsDir"/>),
    /// or <c>null</c> when unresolved — the folder PS3 writes <c>&lt;id&gt;.mdscene</c> files into.</summary>
    public string? LevelsPath =>
        Resolved && ProjectRoot != null ? Path.Combine(ProjectRoot, LevelsDir) : null;

    /// <summary>Resolves the project context using <see cref="PlatformServices.Current"/> for the base
    /// directory and env/file lookups (the desktop head's call). Never throws.</summary>
    public static EditorProjectContext Resolve()
    {
        var platform = PlatformServices.Current;
        return Resolve(
            platform.BaseDirectory,
            name => platform.GetEnvironmentVariable(name),
            platform.FileExists,
            platform.ReadAllText);
    }

    /// <summary>
    /// Pure resolution (see the class doc for the order): env-var primary, walk-up fallback,
    /// unresolved otherwise. All environment / filesystem access is injected, so this is fully
    /// unit-testable with a simulated layout. Never throws — a lookup failure or a malformed manifest
    /// logs a warning and yields <see cref="Unresolved"/>.
    /// </summary>
    /// <param name="baseDirectory">Where the process runs from (the build-output dir); the walk-up
    /// fallback ascends from here.</param>
    /// <param name="getEnvironmentVariable">Environment lookup (null-tolerant per variable).</param>
    /// <param name="fileExists">Existence probe for a candidate manifest path.</param>
    /// <param name="readAllText">Reader for a found manifest.</param>
    /// <param name="maxWalkUpDepth">Bound on ancestor directories the walk-up probes.</param>
    public static EditorProjectContext Resolve(
        string baseDirectory,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        Func<string, string> readAllText,
        int maxWalkUpDepth = DefaultMaxWalkUpDepth)
    {
        if (fileExists == null) throw new ArgumentNullException(nameof(fileExists));
        if (readAllText == null) throw new ArgumentNullException(nameof(readAllText));

        // PRIMARY: the env var names the project/content root; probe Content/ then the bare root.
        var envRoot = getEnvironmentVariable?.Invoke(ProjectRootVariable)?.Trim();
        if (!string.IsNullOrEmpty(envRoot))
        {
            var envManifest = ProbeManifest(envRoot!, fileExists);
            if (envManifest != null)
                return Build(envManifest, readAllText);
            Logger.Warning(
                $"[level-editor] {ProjectRootVariable}='{envRoot}' set but no {GameProject.FileName} " +
                $"at '{Path.Combine(envRoot!, "Content", GameProject.FileName)}' or " +
                $"'{Path.Combine(envRoot!, GameProject.FileName)}'; falling back to walk-up.");
        }

        // FALLBACK: walk up from the base directory looking for a manifest (bare or under Content/).
        var dir = string.IsNullOrEmpty(baseDirectory) ? null : baseDirectory;
        for (var depth = 0; depth < maxWalkUpDepth && !string.IsNullOrEmpty(dir); depth++)
        {
            var manifest = ProbeManifest(dir!, fileExists);
            if (manifest != null)
                return Build(manifest, readAllText);

            var parent = Path.GetDirectoryName(dir!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.Ordinal))
                break; // reached the filesystem root
            dir = parent;
        }

        // UNRESOLVED: shipped build / relocated output / console. Never throws; Save is disabled.
        Logger.Info(
            $"[level-editor] No {GameProject.FileName} resolved (env var {ProjectRootVariable} unset/empty and " +
            $"none found walking up from '{baseDirectory}'); editor Save is disabled until a project root is set.");
        return Unresolved;
    }

    /// <summary>The manifest at <paramref name="root"/>: <c>&lt;root&gt;/Content/game.mdproj</c>
    /// preferred, then <c>&lt;root&gt;/game.mdproj</c>. Returns the first existing path, or null.</summary>
    private static string? ProbeManifest(string root, Func<string, bool> fileExists)
    {
        var underContent = Path.Combine(root, "Content", GameProject.FileName);
        if (fileExists(underContent)) return underContent;
        var atRoot = Path.Combine(root, GameProject.FileName);
        return fileExists(atRoot) ? atRoot : null;
    }

    /// <summary>Loads + parses a found manifest into a resolved context (root = the manifest's
    /// directory). A malformed / unreadable manifest logs a warning and yields
    /// <see cref="Unresolved"/> — never throws.</summary>
    private static EditorProjectContext Build(string manifestPath, Func<string, string> readAllText)
    {
        try
        {
            var manifest = CanonicalJson.Deserialize<GameProject>(readAllText(manifestPath));
            if (manifest == null)
            {
                Logger.Warning($"[level-editor] {GameProject.FileName} at '{manifestPath}' parsed to null; treating the project as unresolved.");
                return Unresolved;
            }
            var projectRoot = Path.GetDirectoryName(manifestPath);
            var levelsDir = string.IsNullOrWhiteSpace(manifest.LevelsDir) ? GameProject.DefaultLevelsDir : manifest.LevelsDir;
            Logger.Info(
                $"[level-editor] Project resolved: root='{projectRoot}', manifest='{manifestPath}', " +
                $"startScene='{manifest.StartScene}', levelsDir='{levelsDir}'.");
            return new EditorProjectContext(true, projectRoot, manifestPath, levelsDir, manifest);
        }
        catch (Exception e)
        {
            Logger.Warning($"[level-editor] Failed to read {GameProject.FileName} at '{manifestPath}' ({e.GetType().Name}: {e.Message}); treating the project as unresolved.");
            return Unresolved;
        }
    }
}
