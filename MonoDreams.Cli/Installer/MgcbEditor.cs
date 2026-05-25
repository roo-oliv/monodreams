using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal static class MgcbEditor
{
    public static void ApplyBlock(string projectDir, BlockManifest manifest)
    {
        if (manifest.MgcbEntries.Count == 0) return;

        var mgcbPath = Path.Combine(projectDir, "Content", "Content.mgcb");
        if (!File.Exists(mgcbPath))
        {
            Console.Error.WriteLine($"  warning: block '{manifest.Name}' has mgcbEntries but '{mgcbPath}' does not exist — skipping. Create it before installing this block.");
            return;
        }

        var lines = File.ReadAllLines(mgcbPath).ToList();
        var marker = $"# --- {manifest.Name} (managed by monodreams-cli) ---";
        if (lines.Contains(marker)) return;

        lines.Add("");
        lines.Add(marker);
        lines.AddRange(manifest.MgcbEntries);
        File.WriteAllLines(mgcbPath, lines);
    }
}
