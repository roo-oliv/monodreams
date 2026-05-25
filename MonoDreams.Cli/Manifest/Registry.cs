using System.Text.Json;

namespace MonoDreams.Cli.Manifest;

internal sealed class Registry
{
    public string EngineRoot { get; }
    public string BlocksDir { get; }
    public RegistryIndex Index { get; }

    private readonly Dictionary<string, BlockManifest> _manifests = new();

    private Registry(string engineRoot, RegistryIndex index)
    {
        EngineRoot = engineRoot;
        BlocksDir = Path.Combine(engineRoot, "MonoDreams");
        Index = index;
    }

    public static Registry Load(string? explicitEngineRoot)
    {
        var engineRoot = Locate(explicitEngineRoot);
        var blocksDir = Path.Combine(engineRoot, "MonoDreams");

        var index = new RegistryIndex();
        foreach (var blockDir in Directory.EnumerateDirectories(blocksDir).OrderBy(d => d))
        {
            var manifestPath = Path.Combine(blockDir, "block.json");
            if (!File.Exists(manifestPath)) continue;
            var m = JsonSerializer.Deserialize<BlockManifest>(File.ReadAllText(manifestPath), JsonOpts.Default)
                    ?? throw new InvalidDataException($"Failed to parse '{manifestPath}'.");
            index.Blocks.Add(new RegistryEntry { Name = m.Name, Description = m.Description });
        }

        var presetsPath = Path.Combine(blocksDir, "presets.json");
        if (File.Exists(presetsPath))
        {
            var presets = JsonSerializer.Deserialize<PresetsFile>(File.ReadAllText(presetsPath), JsonOpts.Default);
            if (presets is not null) index.Presets = presets.Presets;
        }

        return new Registry(engineRoot, index);
    }

    public BlockManifest GetBlock(string name)
    {
        if (_manifests.TryGetValue(name, out var cached)) return cached;
        var path = Path.Combine(BlocksDir, name, "block.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Block '{name}' not found (expected '{path}').");
        var manifest = JsonSerializer.Deserialize<BlockManifest>(File.ReadAllText(path), JsonOpts.Default)
                       ?? throw new InvalidDataException($"Failed to parse '{path}'.");
        _manifests[name] = manifest;
        return manifest;
    }

    public string GetBlockDir(string name) => Path.Combine(BlocksDir, name);

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
        if (HasBlocks(cwd)) return cwd;

        var asmDir = Path.GetDirectoryName(typeof(Registry).Assembly.Location) ?? "";
        if (HasBlocks(asmDir)) return asmDir;

        throw new DirectoryNotFoundException(
            "Could not locate the MonoDreams engine source. Pass --registry <path-to-repo-root>, run from the MonoDreams repo root, or install the tool with bundled engine source.");
    }

    private static bool HasBlocks(string dir)
    {
        var blocksDir = Path.Combine(dir, "MonoDreams");
        if (!Directory.Exists(blocksDir)) return false;
        return Directory.EnumerateFiles(blocksDir, "block.json", SearchOption.AllDirectories).Any();
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
