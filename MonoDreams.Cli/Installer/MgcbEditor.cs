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
    /// </summary>
    public static void ApplyModule(string projectDir, ModuleManifest manifest, IReadOnlyList<Platform> targetPlatforms)
    {
        var entries = manifest.MgcbEntries
            .Where(e => targetPlatforms.Any(e.AppliesTo))
            .Select(e => e.Value)
            .ToList();
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
