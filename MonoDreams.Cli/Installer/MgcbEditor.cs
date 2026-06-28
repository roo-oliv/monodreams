using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal static class MgcbEditor
{
    /// <summary>
    /// Appends a module's content-pipeline lines to <c>Content/Content.mgcb</c>, restricted to the
    /// entries that apply to <paramref name="targetPlatforms"/>. An entry tagged for a single backend
    /// (e.g. a <c>/reference:</c> path that differs per pipeline assembly) is appended only when that
    /// platform is targeted; untagged (backend-agnostic) lines always apply. The same <c>.mgcb</c> is
    /// built per platform at build time (the platform comes from the head csproj, never the .mgcb), so
    /// only backend-specific importer/processor/reference lines need a platform tag.
    /// <para>
    /// <b>Multi-platform limitation:</b> a project that targets more than one platform shares a single
    /// <c>Content.mgcb</c> that is built once per backend. Per-platform-tagged entries from such a module
    /// would therefore land in that one file, and each backend build would also see the *other* backend's
    /// line (the .mgcb has no platform conditioning). Per-platform <c>.mgcb</c> fragments would be the
    /// real fix; until then we emit a warning so the conflict is visible rather than silent. No shipped
    /// module uses object-form (platform-tagged) mgcbEntries today — <c>level-ldtk</c> and <c>dialogue</c>
    /// use bare backend-agnostic strings — so this path is latent.
    /// </para>
    /// </summary>
    public static void ApplyModule(string projectDir, ModuleManifest manifest, IReadOnlyList<Platform> targetPlatforms)
    {
        var applicable = manifest.MgcbEntries
            .Where(e => targetPlatforms.Any(e.AppliesTo))
            .ToList();

        // In a multi-platform project, a per-platform-tagged entry would still go into the single shared
        // .mgcb, where the other backend's build would also process it. Warn (don't silently mix backends).
        if (targetPlatforms.Count > 1)
        {
            var tagged = applicable.Where(e => e.PlatformsRaw is { Count: > 0 }).Select(e => e.Value).ToList();
            if (tagged.Count > 0)
                Console.Error.WriteLine(
                    $"  warning: module '{manifest.Name}' has per-platform mgcbEntries ({string.Join(", ", tagged)}) but this project " +
                    $"targets multiple platforms ({string.Join(" + ", targetPlatforms.Select(Platforms.ToToken))}) sharing one Content.mgcb. " +
                    "Each backend build will also see the other backend's line; review the .mgcb after install.");
        }

        var entries = applicable.Select(e => e.Value).ToList();
        if (entries.Count == 0) return;

        var mgcbPath = Path.Combine(projectDir, "Content", "Content.mgcb");
        if (!File.Exists(mgcbPath))
        {
            Console.Error.WriteLine($"  warning: module '{manifest.Name}' has mgcbEntries but '{mgcbPath}' does not exist — skipping. Create it before installing this module.");
            return;
        }

        var lines = File.ReadAllLines(mgcbPath).ToList();
        var marker = $"# --- {manifest.Name} (managed by monodreams-cli) ---";
        if (lines.Contains(marker)) return;

        lines.Add("");
        lines.Add(marker);
        lines.AddRange(entries);
        File.WriteAllLines(mgcbPath, lines);
    }
}
