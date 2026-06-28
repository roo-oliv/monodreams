using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.Cli.Manifest;

/// <summary>
/// Target platforms a module, NuGet package, or content-pipeline line applies to. Values mirror the
/// <c>$(MonoDreamsPlatform)</c> MSBuild property: <see cref="Desktop"/> resolves the
/// MonoGame.Framework.DesktopGL backend, <see cref="Web"/> resolves nkast.Xna.Framework (KNI BlazorGL).
/// </summary>
internal enum Platform
{
    Desktop,
    Web,
}

internal static class Platforms
{
    /// <summary>Every platform — the default when a manifest omits a platform tag.</summary>
    public static readonly IReadOnlyList<Platform> All = new[] { Platform.Desktop, Platform.Web };

    public static Platform Parse(string value) => value.ToLowerInvariant() switch
    {
        "desktop" => Platform.Desktop,
        "web" => Platform.Web,
        _ => throw new InvalidDataException($"Unknown platform '{value}'. Expected 'desktop' or 'web'."),
    };

    public static string ToToken(Platform platform) => platform switch
    {
        Platform.Desktop => "desktop",
        Platform.Web => "web",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    /// <summary>
    /// Resolves a raw <c>platforms</c> string list (as read from a manifest) into the platform set it
    /// represents. A null or empty list means "all platforms" — the documented default for an omitted tag.
    /// </summary>
    public static IReadOnlyList<Platform> Resolve(List<string>? raw)
        => raw is null || raw.Count == 0 ? All : raw.Select(Parse).Distinct().ToList();
}

internal sealed class ModuleManifest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    /// <summary>
    /// Raw <c>platforms</c> tokens as written in the manifest. Null/empty means all platforms.
    /// Use <see cref="SupportedPlatforms"/> for the resolved set and <see cref="SupportsPlatform"/> to test one.
    /// </summary>
    [JsonPropertyName("platforms")] public List<string>? PlatformsRaw { get; set; }

    [JsonPropertyName("dependencies")] public List<string> Dependencies { get; set; } = new();
    [JsonPropertyName("nugetDependencies")] public List<NugetDep> NugetDependencies { get; set; } = new();
    [JsonPropertyName("csprojProperties")] public Dictionary<string, string> CsprojProperties { get; set; } = new();
    [JsonPropertyName("files")] public List<FileEntry> Files { get; set; } = new();
    [JsonPropertyName("mgcbEntries")] public List<MgcbEntry> MgcbEntries { get; set; } = new();
    [JsonPropertyName("postInstallNotes")] public string? PostInstallNotes { get; set; }
    [JsonPropertyName("agentsMd")] public string? AgentsMd { get; set; }
    [JsonPropertyName("premisesRef")] public string? PremisesRef { get; set; }

    /// <summary>The resolved platform set this module supports (all platforms when untagged).</summary>
    [JsonIgnore] public IReadOnlyList<Platform> SupportedPlatforms => Platforms.Resolve(PlatformsRaw);

    public bool SupportsPlatform(Platform platform) => SupportedPlatforms.Contains(platform);

    /// <summary>NuGet packages this module injects for the given platform (untagged packages apply to all).</summary>
    public IEnumerable<NugetDep> NugetDependenciesFor(Platform platform)
        => NugetDependencies.Where(n => n.AppliesTo(platform));

    /// <summary>Content-pipeline lines this module appends for the given platform (untagged lines apply to all).</summary>
    public IEnumerable<MgcbEntry> MgcbEntriesFor(Platform platform)
        => MgcbEntries.Where(e => e.AppliesTo(platform));
}

internal sealed class NugetDep
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("privateAssets")] public string? PrivateAssets { get; set; }

    /// <summary>Raw <c>platforms</c> tokens; null/empty means all platforms.</summary>
    [JsonPropertyName("platforms")] public List<string>? PlatformsRaw { get; set; }

    [JsonIgnore] public IReadOnlyList<Platform> SupportedPlatforms => Platforms.Resolve(PlatformsRaw);

    public bool AppliesTo(Platform platform) => SupportedPlatforms.Contains(platform);
}

/// <summary>
/// A single MGCB content-pipeline line. The manifest writes it either as a bare string (applies to all
/// platforms) or as an object <c>{ value, platforms }</c> when the importer/processor or <c>/reference:</c>
/// path differs per backend. <see cref="MgcbEntryConverter"/> reads both forms.
/// </summary>
[JsonConverter(typeof(MgcbEntryConverter))]
internal sealed class MgcbEntry
{
    public string Value { get; set; } = "";

    /// <summary>Raw <c>platforms</c> tokens; null/empty means all platforms.</summary>
    public List<string>? PlatformsRaw { get; set; }

    public IReadOnlyList<Platform> SupportedPlatforms => Platforms.Resolve(PlatformsRaw);

    public bool AppliesTo(Platform platform) => SupportedPlatforms.Contains(platform);
}

/// <summary>Reads <c>mgcbEntries</c> items as either a bare string or a <c>{ value, platforms }</c> object.</summary>
internal sealed class MgcbEntryConverter : JsonConverter<MgcbEntry>
{
    public override MgcbEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new MgcbEntry { Value = reader.GetString() ?? "" };

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"mgcbEntries item must be a string or object, got {reader.TokenType}.");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var entry = new MgcbEntry
        {
            Value = root.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "",
        };
        if (root.TryGetProperty("platforms", out var platforms) && platforms.ValueKind == JsonValueKind.Array)
        {
            entry.PlatformsRaw = new List<string>();
            foreach (var p in platforms.EnumerateArray())
                if (p.GetString() is { } s) entry.PlatformsRaw.Add(s);
        }
        return entry;
    }

    public override void Write(Utf8JsonWriter writer, MgcbEntry value, JsonSerializerOptions options)
    {
        if (value.PlatformsRaw is null || value.PlatformsRaw.Count == 0)
        {
            writer.WriteStringValue(value.Value);
            return;
        }
        writer.WriteStartObject();
        writer.WriteString("value", value.Value);
        writer.WritePropertyName("platforms");
        writer.WriteStartArray();
        foreach (var p in value.PlatformsRaw) writer.WriteStringValue(p);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

internal sealed class FileEntry
{
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("destination")] public string Destination { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "code";
}
