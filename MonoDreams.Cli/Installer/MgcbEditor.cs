using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal static class MgcbEditor
{
    public static void ApplyModule(string projectDir, ModuleManifest manifest)
    {
        if (manifest.MgcbEntries.Count == 0) return;

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
        lines.AddRange(manifest.MgcbEntries.Select(e => e.Value));
        File.WriteAllLines(mgcbPath, lines);
    }
}
