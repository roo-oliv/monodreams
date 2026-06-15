using System.Text.Json;

namespace MonoDreams.Cli.Manifest;

internal sealed class Registry
{
    public string EngineRoot { get; }
    public string ModulesDir { get; }
    public RegistryIndex Index { get; }

    private readonly Dictionary<string, ModuleManifest> _manifests = new();

    private Registry(string engineRoot, RegistryIndex index)
    {
        EngineRoot = engineRoot;
        ModulesDir = Path.Combine(engineRoot, "MonoDreams");
        Index = index;
    }

    public static Registry Load(string? explicitEngineRoot)
    {
        var engineRoot = Locate(explicitEngineRoot);
        var modulesDir = Path.Combine(engineRoot, "MonoDreams");

        var index = new RegistryIndex();
        foreach (var moduleDir in Directory.EnumerateDirectories(modulesDir).OrderBy(d => d))
        {
            var manifestPath = Path.Combine(moduleDir, "module.json");
            if (!File.Exists(manifestPath)) continue;
            var m = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(manifestPath), JsonOpts.Default)
                    ?? throw new InvalidDataException($"Failed to parse '{manifestPath}'.");
            index.Modules.Add(new RegistryEntry { Name = m.Name, Description = m.Description });
        }

        var presetsPath = Path.Combine(modulesDir, "presets.json");
        if (File.Exists(presetsPath))
        {
            var presets = JsonSerializer.Deserialize<PresetsFile>(File.ReadAllText(presetsPath), JsonOpts.Default);
            if (presets is not null) index.Presets = presets.Presets;
        }

        return new Registry(engineRoot, index);
    }

    public ModuleManifest GetModule(string name)
    {
        if (_manifests.TryGetValue(name, out var cached)) return cached;
        var path = Path.Combine(ModulesDir, name, "module.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Module '{name}' not found (expected '{path}').");
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(path), JsonOpts.Default)
                       ?? throw new InvalidDataException($"Failed to parse '{path}'.");
        _manifests[name] = manifest;
        return manifest;
    }

    public string GetModuleDir(string name) => Path.Combine(ModulesDir, name);

    public RegistryPreset? GetPreset(string name) =>
        Index.Presets.FirstOrDefault(p => p.Name == name);

    private static string Locate(string? explicitEngineRoot)
    {
        if (!string.IsNullOrEmpty(explicitEngineRoot))
        {
            var full = Path.GetFullPath(explicitEngineRoot);
            if (!Directory.Exists(full))
                throw new DirectoryNotFoundException($"Engine root '{full}' does not exist.");
            return full;
        }

        var cwd = Directory.GetCurrentDirectory();
        if (HasModules(cwd)) return cwd;

        var asmDir = Path.GetDirectoryName(typeof(Registry).Assembly.Location) ?? "";
        if (HasModules(asmDir)) return asmDir;

        throw new DirectoryNotFoundException(
            "Could not locate the MonoDreams engine source. Pass --registry <path-to-repo-root>, run from the MonoDreams repo root, or install the tool with bundled engine source.");
    }

    private static bool HasModules(string dir)
    {
        var modulesDir = Path.Combine(dir, "MonoDreams");
        if (!Directory.Exists(modulesDir)) return false;
        return Directory.EnumerateFiles(modulesDir, "module.json", SearchOption.AllDirectories).Any();
    }
}

internal sealed class PresetsFile
{
    [System.Text.Json.Serialization.JsonPropertyName("presets")]
    public List<RegistryPreset> Presets { get; set; } = new();
}

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
