#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// <para><b>Resolution order (banked decision 1, corrected in FW1)</b> — <see cref="Resolve"/>:</para>
/// <list type="number">
///   <item><b>PRIMARY — env var <see cref="ProjectRootVariable"/>.</b> Set in the dev run
///   configuration (the same one that carries <c>--editor</c>) to the project/content root; the
///   manifest is probed at <c>&lt;root&gt;/Content/game.mdproj</c> then <c>&lt;root&gt;/game.mdproj</c>.
///   This is the primary override to target the <b>source</b> tree (so a Save lands in git). For the
///   reference game it is the absolute path to the content project, e.g.
///   <c>.../MonoDreams.Examples.Core</c>.</item>
///   <item><b>FALLBACK — walk up from <c>BaseDirectory</c>, rejecting build-output copies.</b> When
///   the env var is unset (or names a root with no manifest), ascend from the build-output dir
///   probing <c>&lt;dir&gt;/Content/game.mdproj</c> then <c>&lt;dir&gt;/game.mdproj</c> at each level —
///   but <b>any candidate whose path contains a <c>bin</c>/<c>obj</c> segment is rejected</b> (that is
///   the MGCB-copied OUTPUT manifest beside the executable, never the versioned source). This is the
///   FW1 fix for the "Save lands in bin/…/Content/Levels" bug: the walk-up used to match the output
///   copy first.</item>
///   <item><b>FALLBACK — repo-root search for the SOURCE manifest.</b> The source manifest usually
///   sits in a <i>sibling</i> project (e.g. <c>.../MonoDreams.Examples.Core/Content/game.mdproj</c>),
///   not on the build-output ancestor chain, so the walk-up alone cannot find it. So while walking up
///   we also detect the <b>repository/solution root</b> (an ancestor holding a <c>.git</c> entry — file
///   OR directory, so git worktrees work — or a <c>*.sln</c>), then recursively search under it for
///   <c>game.mdproj</c>, <b>excluding any <c>bin</c>/<c>obj</c> path</b>. When several source manifests
///   exist (e.g. a web head's <c>wwwroot/Content</c> copy) the choice is deterministic: shallowest
///   path first, then ordinal — so a normal <c>dotnet run</c>/Rider run from inside the repo resolves
///   the SOURCE tree with <b>no env var</b>. Set <see cref="ProjectRootVariable"/> to disambiguate.</item>
///   <item><b>UNRESOLVED — only an output copy (or nothing) found</b> (a shipped/relocated build, a
///   console, a malformed manifest). <see cref="Resolved"/> is <c>false</c>; the editor never throws,
///   and Save is disabled with an actionable "no project root" reason — it never silently writes to
///   <c>bin/…</c>.</item>
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
    /// directory and env/file lookups (the desktop head's call). Never throws.
    ///
    /// <para>The repo-root detection + recursive source-manifest search need directory existence and
    /// recursive file enumeration, which the <see cref="IPlatformServices"/> seam does not expose.
    /// Project resolution is a <b>desktop-only, editor-init host concern</b> (it never runs on web — the
    /// web head has no source tree to author into), so these two probes are the module's only direct
    /// <see cref="System.IO"/> access and stay OUT of the pure <see cref="Resolve"/> overload below,
    /// which the tests drive with a simulated filesystem.</para></summary>
    public static EditorProjectContext Resolve()
    {
        var platform = PlatformServices.Current;
        return Resolve(
            platform.BaseDirectory,
            name => platform.GetEnvironmentVariable(name),
            platform.FileExists,
            platform.ReadAllText,
            directoryExists: SafeDirectoryExists,
            enumerateFiles: SafeEnumerateFiles);
    }

    private static bool SafeDirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    /// <summary>Recursive/top-level file enumeration for the desktop resolution, tolerant of an
    /// inaccessible subtree (returns what it can, never throws — a failure just yields no candidates
    /// and the project resolves as unresolved).</summary>
    private static IEnumerable<string> SafeEnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
            };
            return Directory.EnumerateFiles(directory, searchPattern, options).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Pure resolution (see the class doc for the order): env-var primary; then a walk-up fallback that
    /// <b>rejects build-output copies</b> (any candidate under a <c>bin</c>/<c>obj</c> path) while
    /// locating the repository root; then a recursive search under that repo root for the SOURCE
    /// manifest; unresolved otherwise. All environment / filesystem access is injected, so this is fully
    /// unit-testable with a simulated layout. Never throws — a lookup failure or a malformed manifest
    /// logs a warning and yields <see cref="Unresolved"/>.
    /// </summary>
    /// <param name="baseDirectory">Where the process runs from (the build-output dir); the walk-up
    /// fallback ascends from here.</param>
    /// <param name="getEnvironmentVariable">Environment lookup (null-tolerant per variable).</param>
    /// <param name="fileExists">Existence probe for a candidate manifest path (and the <c>.git</c>
    /// repo-root marker file).</param>
    /// <param name="readAllText">Reader for a found manifest.</param>
    /// <param name="directoryExists">Optional directory-existence probe (for a <c>.git</c> directory
    /// repo-root marker). When null the <c>.git</c>-directory check is skipped.</param>
    /// <param name="enumerateFiles">Optional <c>(directory, searchPattern, recursive) =&gt; paths</c>
    /// enumeration. Used to detect a <c>*.sln</c> repo-root marker and to recursively find the source
    /// manifest under the repo root. When null the repo-root source search is skipped (the walk-up
    /// still runs), so the pure injected-delegate tests that only exercise walk-up are unaffected.</param>
    /// <param name="preferProjectDirName">Optional tie-break hint: when several source manifests exist,
    /// one whose path contains this exact directory segment is preferred (else shallowest-then-ordinal).</param>
    /// <param name="maxWalkUpDepth">Bound on ancestor directories the walk-up probes.</param>
    public static EditorProjectContext Resolve(
        string baseDirectory,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> fileExists,
        Func<string, string> readAllText,
        Func<string, bool>? directoryExists = null,
        Func<string, string, bool, IEnumerable<string>>? enumerateFiles = null,
        string? preferProjectDirName = null,
        int maxWalkUpDepth = DefaultMaxWalkUpDepth)
    {
        if (fileExists == null) throw new ArgumentNullException(nameof(fileExists));
        if (readAllText == null) throw new ArgumentNullException(nameof(readAllText));

        // PRIMARY: the env var names the project/content root; probe Content/ then the bare root. The
        // env var is a trusted explicit override, so it is NOT bin/obj-filtered (the user chose it).
        var envRoot = getEnvironmentVariable?.Invoke(ProjectRootVariable)?.Trim();
        if (!string.IsNullOrEmpty(envRoot))
        {
            var envManifest = ProbeManifest(envRoot!, fileExists);
            if (envManifest != null)
                return Build(envManifest, readAllText);
            Logger.Warning(
                $"[level-editor] {ProjectRootVariable}='{envRoot}' set but no {GameProject.FileName} " +
                $"at '{Path.Combine(envRoot!, "Content", GameProject.FileName)}' or " +
                $"'{Path.Combine(envRoot!, GameProject.FileName)}'; falling back to walk-up + repo search.");
        }

        // FALLBACK 1: walk up from the base directory looking for a SOURCE manifest (bare or under
        // Content/), REJECTING any candidate whose path contains a bin/obj segment — that is the MGCB
        // build-output copy beside the executable, never the versioned source. Along the way, remember
        // the first repository/solution root (a .git entry or a *.sln) for the sibling-search fallback.
        string? repoRoot = null;
        var dir = string.IsNullOrEmpty(baseDirectory) ? null : baseDirectory;
        for (var depth = 0; depth < maxWalkUpDepth && !string.IsNullOrEmpty(dir); depth++)
        {
            var manifest = ProbeManifest(dir!, fileExists);
            if (manifest != null && !IsInBinOrObj(manifest))
                return Build(manifest, readAllText);

            if (repoRoot == null && IsRepoRoot(dir!, fileExists, directoryExists, enumerateFiles))
                repoRoot = dir;

            var parent = Path.GetDirectoryName(dir!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.Ordinal))
                break; // reached the filesystem root
            dir = parent;
        }

        // FALLBACK 2: the source manifest usually lives in a SIBLING project (e.g.
        // <Game>.Core/Content/game.mdproj), off the build-output ancestor chain — so search the whole
        // repo for game.mdproj, excluding every bin/obj path (a normal in-repo run resolves the source
        // with no env var).
        if (repoRoot != null && enumerateFiles != null)
        {
            var sourceManifest = FindSourceManifest(repoRoot, enumerateFiles, preferProjectDirName);
            if (sourceManifest != null)
                return Build(sourceManifest, readAllText);
        }

        // UNRESOLVED: only an output copy (or nothing) found — a shipped build / relocated output /
        // console. Never throws; Save is disabled with an actionable, named reason (never writes to bin).
        Logger.Info(
            $"[level-editor] No SOURCE {GameProject.FileName} resolved (env var {ProjectRootVariable} " +
            $"unset/empty, no non-bin/obj manifest walking up from '{baseDirectory}', and no source " +
            $"manifest found under the repo root). Editor Save is disabled until a project root is set. " +
            $"Set {ProjectRootVariable} to your content project directory (e.g. the absolute path to " +
            $"'MonoDreams.Examples.Core', which contains Content/{GameProject.FileName}).");
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

    /// <summary>Whether <paramref name="path"/> has a <c>bin</c> or <c>obj</c> directory <b>segment</b>
    /// (a build-output copy). Segment-exact (case-insensitive), so <c>My.Bin.Game</c> or
    /// <c>MyGame.Core</c> is NOT rejected — only a literal <c>.../bin/...</c> or <c>.../obj/...</c>.</summary>
    private static bool IsInBinOrObj(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var s in segments)
            if (string.Equals(s, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "obj", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Whether <paramref name="dir"/> is a repository/solution root — it holds a <c>.git</c>
    /// entry (a FILE, as in a git worktree, or a directory) or a <c>*.sln</c>. Any missing probe
    /// delegate is simply treated as "no marker of that kind".</summary>
    private static bool IsRepoRoot(string dir, Func<string, bool> fileExists,
        Func<string, bool>? directoryExists, Func<string, string, bool, IEnumerable<string>>? enumerateFiles)
    {
        var gitPath = Path.Combine(dir, ".git");
        if (fileExists(gitPath)) return true;                       // .git file (git worktree)
        if (directoryExists != null && directoryExists(gitPath)) return true; // .git directory
        if (enumerateFiles != null && enumerateFiles(dir, "*.sln", false).Any()) return true;
        return false;
    }

    /// <summary>Recursively finds the SOURCE manifest under <paramref name="repoRoot"/> — every
    /// <c>game.mdproj</c>, excluding any <c>bin</c>/<c>obj</c> path. When several exist (e.g. a web head's
    /// <c>wwwroot/Content</c> copy), the choice is deterministic: a match on
    /// <paramref name="preferProjectDirName"/> first, then the shallowest path, then ordinal. Returns
    /// null when none (outside a bin/obj copy) is found.</summary>
    private static string? FindSourceManifest(string repoRoot,
        Func<string, string, bool, IEnumerable<string>> enumerateFiles, string? preferProjectDirName)
    {
        var candidates = enumerateFiles(repoRoot, GameProject.FileName, true)
            .Where(p => !IsInBinOrObj(p))
            .ToList();
        if (candidates.Count == 0) return null;

        return candidates
            .OrderByDescending(p => preferProjectDirName != null && HasDirectorySegment(p, preferProjectDirName))
            .ThenBy(SegmentCount)
            .ThenBy(p => p, StringComparer.Ordinal)
            .First();
    }

    private static bool HasDirectorySegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase));

    private static int SegmentCount(string path) =>
        path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

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
