using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal sealed class Installer
{
    private readonly Registry _registry;
    private readonly string _projectDir;
    private readonly bool _dryRun;
    private readonly IReadOnlyList<Platform> _targetPlatforms;

    public Installer(Registry registry, string projectDir, bool dryRun, IReadOnlyList<Platform>? targetPlatforms = null)
    {
        _registry = registry;
        _projectDir = Path.GetFullPath(projectDir);
        _dryRun = dryRun;
        // Default to desktop-only when unspecified — the historical single-platform behavior.
        _targetPlatforms = targetPlatforms is { Count: > 0 } ? targetPlatforms : new[] { Platform.Desktop };
    }

    public void Apply(ModuleManifest manifest)
    {
        var prefix = _dryRun ? "would " : "";
        Console.WriteLine($"  {prefix}install {manifest.Name} — {manifest.Description}");

        foreach (var file in EnumerateFiles(manifest))
        {
            var src = Path.Combine(_registry.EngineRoot, file.Source);
            var dst = Path.Combine(_projectDir, file.Destination);
            if (!File.Exists(src))
                throw new FileNotFoundException($"Module '{manifest.Name}': source file '{src}' not found.");

            Console.WriteLine($"    {prefix}copy {file.Source} -> {file.Destination}");
            if (_dryRun) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }

        // Count only the entries that apply to the project's target platform(s) — a desktop-only
        // project must not see web packages in its install plan, and vice versa.
        var nugetCount = manifest.NugetDependencies.Count(n => _targetPlatforms.Any(n.AppliesTo));
        if (nugetCount > 0 || manifest.CsprojProperties.Count > 0)
        {
            var csproj = FindCsproj();
            Console.WriteLine($"    {prefix}edit {Path.GetFileName(csproj)} (+{nugetCount} nuget, +{manifest.CsprojProperties.Count} props)");
            if (!_dryRun) CsprojEditor.ApplyModule(csproj, manifest, _targetPlatforms);
        }

        var mgcbCount = manifest.MgcbEntries.Count(e => _targetPlatforms.Any(e.AppliesTo));
        if (mgcbCount > 0)
        {
            Console.WriteLine($"    {prefix}append {mgcbCount} mgcb entr{(mgcbCount == 1 ? "y" : "ies")} to Content/Content.mgcb");
            if (!_dryRun) MgcbEditor.ApplyModule(_projectDir, manifest, _targetPlatforms);
        }
    }

    private IEnumerable<FileEntry> EnumerateFiles(ModuleManifest manifest)
    {
        if (manifest.Files.Count > 0)
            return manifest.Files;

        var moduleDir = _registry.GetModuleDir(manifest.Name);
        if (!Directory.Exists(moduleDir)) return Array.Empty<FileEntry>();

        var result = new List<FileEntry>();
        foreach (var path in Directory.EnumerateFiles(moduleDir, "*", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var fileName = Path.GetFileName(path);
            if (fileName == "module.json") continue;

            var relFromModule = Path.GetRelativePath(moduleDir, path).Replace('\\', '/');
            // A module ships a `demo/` folder for MonoDreams.Demos (block-demo screens that cross-reference
            // other modules' types and the Demos host). Those do not belong in a consumer project and would
            // not compile there, so they are not copied. The module's own components/systems/messages and
            // `docs/` (markdown, harmless) are copied.
            if (relFromModule.StartsWith("demo/", StringComparison.OrdinalIgnoreCase)) continue;
            // Build outputs are never module source. A registry is usually a *source checkout*, and a module
            // may contain a buildable project of its own (level-ldtk vendors the LDtkMonogame sources, .csproj
            // included), so a contributor's local build leaves bin/ + obj/ inside the module directory. Copying
            // those lands generated AssemblyInfo.cs files inside the user's compile glob and their very first
            // `dotnet build` fails with CS0579 (duplicate assembly attributes) — found by the manifest-honesty
            // check (issue #83), which builds what `add` produces.
            if (relFromModule.Split('/').Any(segment =>
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("obj", StringComparison.OrdinalIgnoreCase))) continue;

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
        // The shared game library is where engine source + shared NuGet packages land. A multi-project
        // scaffold lays it out as <Name>.Core; module installs target that, not the per-platform heads
        // (which carry only the backend framework + the entry point). Prefer the Core project, whether
        // it lives directly in the project dir or in a sibling <Name>.Core/ subdirectory.
        var topLevel = Directory.GetFiles(_projectDir, "*.csproj", SearchOption.TopDirectoryOnly);
        var coreInDir = topLevel.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).EndsWith(".Core", StringComparison.OrdinalIgnoreCase));
        if (coreInDir is not null) return coreInDir;

        var coreSubdir = Directory.GetDirectories(_projectDir, "*.Core")
            .SelectMany(d => Directory.GetFiles(d, "*.csproj", SearchOption.TopDirectoryOnly))
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).EndsWith(".Core", StringComparison.OrdinalIgnoreCase));
        if (coreSubdir is not null) return coreSubdir;

        return topLevel.Length switch
        {
            0 => throw new FileNotFoundException($"No .csproj found in '{_projectDir}'. Run `monodreams init` first."),
            1 => topLevel[0],
            _ => throw new InvalidOperationException(
                $"Multiple .csproj files found in '{_projectDir}': {string.Join(", ", topLevel.Select(Path.GetFileName))}. Pass --dir <dir> with the specific project.")
        };
    }
}
