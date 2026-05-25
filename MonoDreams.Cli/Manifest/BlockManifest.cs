using System.Text.Json.Serialization;

namespace MonoDreams.Cli.Manifest;

internal sealed class BlockManifest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("dependencies")] public List<string> Dependencies { get; set; } = new();
    [JsonPropertyName("nugetDependencies")] public List<NugetDep> NugetDependencies { get; set; } = new();
    [JsonPropertyName("csprojProperties")] public Dictionary<string, string> CsprojProperties { get; set; } = new();
    [JsonPropertyName("files")] public List<FileEntry> Files { get; set; } = new();
    [JsonPropertyName("mgcbEntries")] public List<string> MgcbEntries { get; set; } = new();
    [JsonPropertyName("postInstallNotes")] public string? PostInstallNotes { get; set; }
    [JsonPropertyName("agentsMd")] public string? AgentsMd { get; set; }
    [JsonPropertyName("premisesRef")] public string? PremisesRef { get; set; }
}

internal sealed class NugetDep
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("privateAssets")] public string? PrivateAssets { get; set; }
}

internal sealed class FileEntry
{
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("destination")] public string Destination { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "code";
}
