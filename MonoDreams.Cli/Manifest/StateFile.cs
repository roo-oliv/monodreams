using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.Cli.Manifest;

internal sealed class StateFile
{
    public const string FileName = "monodreams.json";

    [JsonPropertyName("schema")] public string Schema { get; set; } = "https://monodreams.dev/state.schema.json";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("modules")] public List<string> Modules { get; set; } = new();
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }

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
