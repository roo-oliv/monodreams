using System.Text.Json.Serialization;

namespace MonoDreams.Cli.Manifest;

internal sealed class RegistryIndex
{
    [JsonPropertyName("blocks")] public List<RegistryEntry> Blocks { get; set; } = new();
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
    [JsonPropertyName("blocks")] public List<string> Blocks { get; set; } = new();
}
