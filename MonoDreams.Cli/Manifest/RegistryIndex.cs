using System.Text.Json.Serialization;

namespace MonoDreams.Cli.Manifest;

internal sealed class RegistryIndex
{
    [JsonPropertyName("modules")] public List<RegistryEntry> Modules { get; set; } = new();
    [JsonPropertyName("presets")] public List<RegistryPreset> Presets { get; set; } = new();
}

internal sealed class RegistryEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

internal sealed class RegistryPreset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("modules")] public List<string> Modules { get; set; } = new();

    /// <summary>Raw <c>platforms</c> tokens; null/empty means all platforms.</summary>
    [JsonPropertyName("platforms")] public List<string>? PlatformsRaw { get; set; }

    [JsonIgnore] public IReadOnlyList<Platform> SupportedPlatforms => Platforms.Resolve(PlatformsRaw);

    public bool SupportsPlatform(Platform platform) => SupportedPlatforms.Contains(platform);
}
