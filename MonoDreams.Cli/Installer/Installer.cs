using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal sealed class Installer
{
    private readonly Registry _registry;
    private readonly string _projectDir;
    private readonly bool _dryRun;

    public Installer(Registry registry, string projectDir, bool dryRun)
    {
        _registry = registry;
        _projectDir = Path.GetFullPath(projectDir);
        _dryRun = dryRun;
    }

    public void Apply(BlockManifest manifest)
    {
        var prefix = _dryRun ? "would " : "";
        Console.WriteLine($"  {prefix}install {manifest.Name} — {manifest.Description}");

        foreach (var file in EnumerateFiles(manifest))
        {
            var src = Path.Combine(_registry.EngineRoot, file.Source);
            var dst = Path.Combine(_projectDir, file.Destination);
            if (!File.Exists(src))
                throw new FileNotFoundException($"Block '{manifest.Name}': source file '{src}' not found.");

            Console.WriteLine($"    {prefix}copy {file.Source} -> {file.Destination}");
            if (_dryRun) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }

        if (manifest.NugetDependencies.Count > 0 || manifest.CsprojProperties.Count > 0)
        {
            var csproj = FindCsproj();
            Console.WriteLine($"    {prefix}edit {Path.GetFileName(csproj)} (+{manifest.NugetDependencies.Count} nuget, +{manifest.CsprojProperties.Count} props)");
            if (!_dryRun) CsprojEditor.ApplyBlock(csproj, manifest);
        }

        if (manifest.MgcbEntries.Count > 0)
        {
            Console.WriteLine($"    {prefix}append {manifest.MgcbEntries.Count} mgcb entr{(manifest.MgcbEntries.Count == 1 ? "y" : "ies")} to Content/Content.mgcb");
            if (!_dryRun) MgcbEditor.ApplyBlock(_projectDir, manifest);
        }
    }

    private IEnumerable<FileEntry> EnumerateFiles(BlockManifest manifest)
    {
        if (manifest.Files.Count > 0)
            return manifest.Files;

        var blockDir = _registry.GetBlockDir(manifest.Name);
        if (!Directory.Exists(blockDir)) return Array.Empty<FileEntry>();

        var result = new List<FileEntry>();
        foreach (var path in Directory.EnumerateFiles(blockDir, "*", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var fileName = Path.GetFileName(path);
            if (fileName == "block.json") continue;
            var relFromEngine = Path.GetRelativePath(_registry.EngineRoot, path).Replace('\\', '/');
            result.Add(new FileEntry
            {
                Source = relFromEngine,
                Destination = relFromEngine,
                Kind = fileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? "tool" : "code"
            });
        }
        return result;
    }

    private string FindCsproj()
    {
        var matches = Directory.GetFiles(_projectDir, "*.csproj", SearchOption.TopDirectoryOnly);
        return matches.Length switch
        {
            0 => throw new FileNotFoundException($"No .csproj found in '{_projectDir}'. Run `monodreams init` first."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple .csproj files found in '{_projectDir}': {string.Join(", ", matches.Select(Path.GetFileName))}. Pass --project <dir> with the specific project.")
        };
    }
}
