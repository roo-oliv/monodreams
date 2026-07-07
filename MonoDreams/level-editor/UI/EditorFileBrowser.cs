#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// A directory's <b>raw</b> contents as the injected lister returns them — subfolder names and every
/// file name (extensions intact). The browser applies the <c>.mdscene</c> filter + folder/file
/// classification on top (see <see cref="EditorFileBrowser"/>), so the lister only has to do the one
/// thing that genuinely needs the filesystem: split entries into directories vs files.
/// </summary>
public readonly record struct RawDirectory(
    bool Resolved,
    IReadOnlyList<string> Directories,
    IReadOnlyList<string> Files,
    string? Message);

/// <summary>
/// Where the browser opens (and the boundary it may not climb above): the resolved project root is
/// the <b>up-boundary</b>, <see cref="InitialDir"/> is where the browser first lands (the scenes
/// dir), and an unresolved project carries the actionable message shown instead of a listing.
/// </summary>
public readonly record struct BrowserRoots(bool Resolved, string? Root, string? InitialDir, string? Message);

/// <summary>
/// The editor Save/Load dialog's <b>file-system navigation model</b> — the Blender-style directory
/// browser's brain, kept pure (no world, no GraphicsDevice, no direct filesystem) so its whole
/// behaviour is unit-testable by injecting a fake <c>listDirectory</c>. It classifies a directory's
/// contents into <b>subfolders</b> (navigable) and <b>scene files</b> (the <c>.mdscene</c> ids —
/// everything else is filtered out), tracks the <see cref="CurrentDir"/>, and lets the browser
/// descend (<see cref="Enter"/>) and climb (<see cref="Up"/>).
///
/// <para><b>Scoping (why it is not a free OS file picker).</b> The browser is <b>rooted (up-bounded)
/// at the project root</b> — <see cref="Up"/> stops there and never escapes into the wider OS
/// filesystem. It <b>opens at the project's scenes dir</b> (<c>EditorProjectContext.LevelsPath</c> =
/// <c>Content/Levels</c>), because per the persistence design a scene must live under
/// <c>Content/Levels</c> to be MGCB-<c>/copy:</c>-bundled into the title and loaded native-first by
/// the shipped game (see the level-editor persistence premises). Navigating up to <c>Content/</c> or
/// the project root is allowed for orientation, but the scenes dir is the shippable home — which is
/// why the browser both starts there and (in the overlay) only auto-bundles a save written directly
/// into it. Exposing the whole disk would let a designer save a scene somewhere the build can never
/// reach it.</para>
/// </summary>
public sealed class EditorFileBrowser
{
    /// <summary>The native scene extension the file filter keeps (mirrors
    /// <see cref="SceneWriter.SceneFileExtension"/>).</summary>
    public const string SceneExtension = SceneWriter.SceneFileExtension;

    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private readonly Func<string, RawDirectory> _listDirectory;

    /// <param name="listDirectory">Given an absolute directory path, returns its raw contents
    /// (subfolder names + every file name). The overlay wires this to <c>System.IO.Directory</c>;
    /// tests inject a fake map. It is the browser's ONLY filesystem seam.</param>
    public EditorFileBrowser(Func<string, RawDirectory> listDirectory)
    {
        _listDirectory = listDirectory ?? throw new ArgumentNullException(nameof(listDirectory));
    }

    /// <summary>Whether the project (and therefore a browsable tree) resolved. When false,
    /// <see cref="Message"/> carries the actionable "set MONODREAMS_PROJECT_ROOT" text.</summary>
    public bool Resolved { get; private set; }

    /// <summary>The up-boundary: <see cref="Up"/> never climbs above this directory.</summary>
    public string? Root { get; private set; }

    /// <summary>The directory whose contents are currently listed (null when unresolved).</summary>
    public string? CurrentDir { get; private set; }

    /// <summary>An informational message to show instead of (or alongside) a listing — the
    /// unresolved-project text, an empty-folder note, or a listing error.</summary>
    public string? Message { get; private set; }

    /// <summary>The current directory's subfolder names, sorted (case-insensitive).</summary>
    public IReadOnlyList<string> Directories { get; private set; } = Array.Empty<string>();

    /// <summary>The current directory's scene ids — <c>.mdscene</c> files only, extension stripped,
    /// sorted (case-insensitive). The <c>.mdscene</c> filter lives here (the model), not the lister.</summary>
    public IReadOnlyList<string> Files { get; private set; } = Array.Empty<string>();

    /// <summary>Total rows the browser lists: folders first, then scene files.</summary>
    public int EntryCount => Directories.Count + Files.Count;

    /// <summary>True when <paramref name="index"/> addresses a folder row (folders precede files).</summary>
    public bool IsDirectory(int index) => index >= 0 && index < Directories.Count;

    /// <summary>Whether <see cref="Up"/> would do anything — i.e. the current directory is a strict
    /// descendant of <see cref="Root"/> (the boundary). At the root it is false.</summary>
    public bool CanGoUp =>
        Resolved && CurrentDir != null && Root != null && !PathEquals(CurrentDir, Root) && IsUnderRoot(CurrentDir);

    /// <summary>Opens the browser against <paramref name="roots"/>: unresolved → shows the message and
    /// lists nothing; resolved → lands at <see cref="BrowserRoots.InitialDir"/> (falling back to the
    /// root) and lists it.</summary>
    public void Open(BrowserRoots roots)
    {
        Resolved = roots.Resolved;
        if (!roots.Resolved)
        {
            Root = CurrentDir = null;
            Directories = Files = Array.Empty<string>();
            Message = roots.Message ?? "No project root resolved.";
            return;
        }

        Root = Normalize(roots.Root);
        var initial = Normalize(roots.InitialDir) ?? Root;
        // Clamp the initial dir into the root boundary (defensive: an InitialDir outside the root
        // would let the very first Up climb past it).
        CurrentDir = initial != null && Root != null && IsWithinOrEqual(initial, Root) ? initial : Root;
        Refresh();
    }

    /// <summary>Descends into the listed subfolder <paramref name="directoryName"/> and lists it.
    /// A no-op (returns false) for an empty name or a name not among <see cref="Directories"/>.</summary>
    public bool Enter(string directoryName)
    {
        if (!Resolved || CurrentDir == null || string.IsNullOrEmpty(directoryName)) return false;
        if (!Directories.Any(d => string.Equals(d, directoryName, StringComparison.OrdinalIgnoreCase)))
            return false;
        CurrentDir = Normalize(Path.Combine(CurrentDir, directoryName));
        Refresh();
        return true;
    }

    /// <summary>Climbs to the parent directory, <b>bounded at <see cref="Root"/></b> (a no-op there).</summary>
    public void Up()
    {
        if (!CanGoUp) return;
        var parent = Normalize(Path.GetDirectoryName(TrimTrailing(CurrentDir!)));
        // Never climb above the root, even if the parent chain somehow steps outside it.
        CurrentDir = parent != null && Root != null && IsWithinOrEqual(parent, Root) ? parent : Root;
        Refresh();
    }

    /// <summary>Re-lists the current directory (after a navigation, or an external change).</summary>
    public void Refresh()
    {
        if (!Resolved || CurrentDir == null)
        {
            Directories = Files = Array.Empty<string>();
            return;
        }

        var raw = _listDirectory(CurrentDir);
        Message = raw.Message;
        Directories = (raw.Directories ?? Array.Empty<string>())
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Files = (raw.Files ?? Array.Empty<string>())
            .Where(f => f.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The absolute path a scene id maps to in the current directory
    /// (<c>&lt;CurrentDir&gt;/&lt;id&gt;.mdscene</c>), or null when unresolved. Used by both Load
    /// (pick a file) and Save (write the named file into the browsed dir).</summary>
    public string? FilePath(string sceneId)
    {
        if (!Resolved || CurrentDir == null || string.IsNullOrEmpty(sceneId)) return null;
        return Path.Combine(CurrentDir, sceneId + SceneExtension);
    }

    /// <summary>The breadcrumb segments from the root's own leaf down to the current directory
    /// (e.g. <c>[Content, Levels, props]</c>). A UI-facing display detail — the model computes it so
    /// the layout stays pure geometry.</summary>
    public IReadOnlyList<string> Breadcrumb
    {
        get
        {
            if (!Resolved || CurrentDir == null || Root == null) return Array.Empty<string>();
            var segments = new List<string>();
            var cur = CurrentDir;
            // Walk up to (and including) the root, collecting leaf names, then reverse.
            while (cur != null)
            {
                var leaf = Path.GetFileName(TrimTrailing(cur));
                segments.Add(string.IsNullOrEmpty(leaf) ? cur : leaf);
                if (PathEquals(cur, Root)) break;
                var parent = Normalize(Path.GetDirectoryName(TrimTrailing(cur)));
                if (parent == null || PathEquals(parent, cur)) break;
                cur = parent;
            }
            segments.Reverse();
            return segments;
        }
    }

    /// <summary>The breadcrumb as a single display string (<c>"Content / Levels / props"</c>).</summary>
    public string BreadcrumbText => string.Join(" / ", Breadcrumb);

    // ─── path helpers (pure string ops — no disk access) ─────────────────────────────────────────

    private bool IsUnderRoot(string dir) => Root != null && IsWithinOrEqual(dir, Root) && !PathEquals(dir, Root);

    private static bool IsWithinOrEqual(string path, string root)
    {
        if (PathEquals(path, root)) return true;
        var trimmedPath = TrimTrailing(path);
        var trimmedRoot = TrimTrailing(root);
        return trimmedPath.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, PathComparison)
            || trimmedPath.StartsWith(trimmedRoot + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static bool PathEquals(string? a, string? b) =>
        string.Equals(TrimTrailing(a), TrimTrailing(b), PathComparison);

    private static string? Normalize(string? path) => string.IsNullOrEmpty(path) ? path : TrimTrailing(path);

    private static string TrimTrailing(string? path) => path?.TrimEnd(Separators) ?? string.Empty;

    /// <summary>Path comparison: case-insensitive on Windows/macOS (the desktop editor hosts), which
    /// matches how the resolved paths compare there.</summary>
    private static StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;
}
