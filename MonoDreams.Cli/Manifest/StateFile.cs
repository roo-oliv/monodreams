using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.Cli.Manifest;

internal sealed class StateFile
{
    public const string FileName = "monodreams.json";

    [JsonPropertyName("schema")] public string Schema { get; set; } = "https://monodreams.dev/state.schema.json";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    /// <summary>
    /// Target platform(s) this project was scaffolded for, as <c>desktop</c> / <c>web</c> tokens.
    /// <c>monodreams init --platform desktop|web|multi</c> records the selection here (multi = both);
    /// <c>monodreams add</c> reads it to inject only the per-platform package variants the project
    /// targets and to warn when a requested module does not support one of those platforms. A missing
    /// or empty list is treated as desktop-only (the historical single-platform default).
    /// </summary>
    [JsonPropertyName("platforms")] public List<string> Platforms { get; set; } = new();

    [JsonPropertyName("modules")] public List<string> Modules { get; set; } = new();
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }

    /// <summary>
    /// Resolved target platforms. Falls back to desktop-only when the file omits the field
    /// (projects scaffolded before platform tracking existed).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Platform> TargetPlatforms =>
        Platforms.Count == 0 ? new[] { Platform.Desktop } : Platforms.Select(MonoDreams.Cli.Manifest.Platforms.Parse).Distinct().ToList();

    public static StateFile LoadOrCreate(string projectDir)
    {
        var path = Path.Combine(projectDir, FileName);
        if (!File.Exists(path)) return new StateFile { CreatedAt = DateTime.UtcNow.ToString("O") };
        return JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path), JsonOpts.Default)
               ?? new StateFile { CreatedAt = DateTime.UtcNow.ToString("O") };
    }

    public void Save(string projectDir)
    {
        UpdatedAt = DateTime.UtcNow.ToString("O");
        CreatedAt ??= UpdatedAt;
        var path = Path.Combine(projectDir, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts.Pretty));
    }
}
