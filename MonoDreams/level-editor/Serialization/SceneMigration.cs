#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>umbrella scene migrator</b> — the engine core behind the <c>monodreams migrate &lt;file|dir&gt;</c>
/// CLI command. It applies <b>every known lift in version order</b> to one file: first the v1→v2 collider
/// lift (<see cref="ColliderMigration"/>), then the v2→v3 camera lift (<see cref="CameraMigration"/>). Each
/// lift is idempotent (a file already at or beyond its target version is a no-op), so a v1 file goes
/// <b>straight to v3</b> in one pass, a v2 file gets only the camera lift, and a v3 file is untouched.
///
/// <para>Because every lift emits through <see cref="CanonicalJson"/>, the umbrella's output is
/// <b>byte-canonical</b> and a <c>migrate → load → save</c> is a <b>strict byte fixed point</b> (unlike a
/// single lift, which stamps only its own target version). This is the recommended command for users; the
/// single-lift <c>migrate-colliders</c> command remains (it runs only the collider lift), but
/// <c>migrate</c> supersedes it — it does everything <c>migrate-colliders</c> does and then finishes the
/// chain to the current version.</para>
///
/// <para>Prefab (<c>.mdprefab</c>) vs scene (<c>.mdscene</c>) is decided by the file EXTENSION: the camera
/// lift never adds a camera entity to a prefab (a prefab is a class — no camera). Unparseable JSON throws
/// loud; a missing path throws <see cref="FileNotFoundException"/>. Dev-time tool only (uses
/// <see cref="File"/> directly, like the importer), never the game.</para>
/// </summary>
public static class SceneMigration
{
    /// <summary>The native file extensions the directory walk migrates.</summary>
    internal static readonly string[] MigratableExtensions = { ".mdscene", ".mdprefab" };

    /// <summary>Outcome of migrating one file's content through the full lift chain.</summary>
    public sealed class Result
    {
        /// <summary>The (possibly rewritten) canonical JSON. Equals the input verbatim when nothing ran.</summary>
        public required string Json { get; init; }

        /// <summary>Whether the bytes changed (any lift ran).</summary>
        public required bool Changed { get; init; }

        /// <summary>Whether the input was already at the current version (every lift a no-op).</summary>
        public required bool AlreadyCurrent { get; init; }

        /// <summary>The collider lift's outcome (v1→v2).</summary>
        public required ColliderMigration.Result Collider { get; init; }

        /// <summary>The camera lift's outcome (v2→v3).</summary>
        public required CameraMigration.Result Camera { get; init; }

        /// <summary>Human-readable one-liners for the lifts that actually ran, in order (empty when the file
        /// was already current). Used by the CLI's per-file summary.</summary>
        public IReadOnlyList<string> LiftsApplied
        {
            get
            {
                var lifts = new List<string>();
                if (Collider.Changed)
                    lifts.Add($"colliders v1→v2 ({Collider.BoxesReshapedInPlace} box(es) reshaped in place, " +
                              $"{Collider.ChildEntitiesAdded} collider child entity(ies) added)");
                if (Camera.Changed)
                {
                    var how = Camera.IsPrefab ? "version bump only (prefab — no camera)"
                        : Camera.CameraBlockLifted ? "camera block lifted into a 'Camera' entity"
                        : Camera.DefaultCameraAdded ? "default 'Camera' entity added at the origin"
                        : Camera.CameraBlockDropped ? "stray camera block dropped (a camera entity already existed)"
                        : "version bump";
                    lifts.Add($"camera v2→v3 ({how})");
                }
                return lifts;
            }
        }
    }

    /// <summary>
    /// Migrates one file's JSON through the full lift chain (colliders then camera), in version order.
    /// Returns the final canonical bytes plus each lift's sub-result. Throws
    /// <see cref="InvalidOperationException"/> on unparseable input.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <param name="sourceName">A display name for the file (used in thrown error messages).</param>
    /// <param name="isPrefab">Whether the file is a <c>.mdprefab</c> (the camera lift adds no camera to it).</param>
    public static Result Migrate(string json, string sourceName, bool isPrefab = false)
    {
        var collider = ColliderMigration.Migrate(json, sourceName);
        var camera = CameraMigration.Migrate(collider.Json, sourceName, isPrefab);
        return new Result
        {
            Json = camera.Json,
            Changed = collider.Changed || camera.Changed,
            AlreadyCurrent = collider.AlreadyCurrent && camera.AlreadyCurrent,
            Collider = collider,
            Camera = camera,
        };
    }

    /// <summary>Per-file outcome for the CLI summary.</summary>
    public sealed class FileReport
    {
        public required string Path { get; init; }
        public required Result Result { get; init; }
        /// <summary>Whether the file was (or would be, under dry-run) written.</summary>
        public required bool Written { get; init; }
    }

    /// <summary>
    /// Migrates a single file at <paramref name="path"/> (reads, migrates, writes back unless
    /// <paramref name="dryRun"/>). Prefab-ness is inferred from the extension. Returns the per-file report.
    /// Throws on unparseable input.
    /// </summary>
    public static FileReport MigrateFile(string path, bool dryRun)
    {
        var isPrefab = string.Equals(Path.GetExtension(path), ".mdprefab", StringComparison.OrdinalIgnoreCase);
        var json = File.ReadAllText(path);
        var result = Migrate(json, path, isPrefab);
        var willWrite = result.Changed && !dryRun;
        if (willWrite) File.WriteAllText(path, result.Json);
        return new FileReport { Path = path, Result = result, Written = willWrite };
    }

    /// <summary>
    /// Recursively migrates every <c>.mdscene</c>/<c>.mdprefab</c> under <paramref name="dir"/> (sorted for
    /// deterministic output). Returns one report per file.
    /// </summary>
    public static IReadOnlyList<FileReport> MigrateDirectory(string dir, bool dryRun)
    {
        var reports = new List<FileReport>();
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => MigratableExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var file in files)
            reports.Add(MigrateFile(file, dryRun));
        return reports;
    }

    /// <summary>Dispatches a path to <see cref="MigrateFile"/> (a file) or <see cref="MigrateDirectory"/>
    /// (a directory). Throws <see cref="FileNotFoundException"/> when the path does not exist.</summary>
    public static IReadOnlyList<FileReport> MigratePath(string path, bool dryRun)
    {
        if (Directory.Exists(path)) return MigrateDirectory(path, dryRun);
        if (File.Exists(path)) return new[] { MigrateFile(path, dryRun) };
        throw new FileNotFoundException($"[migrate] Path not found: '{path}'.", path);
    }
}
