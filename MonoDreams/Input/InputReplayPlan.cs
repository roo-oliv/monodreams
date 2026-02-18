using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.Input;

public class InputReplayPlan
{
    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("startLevel")]
    public string StartLevel { get; set; }

    [JsonPropertyName("commands")]
    public List<InputReplayCommand> Commands { get; set; }

    [JsonPropertyName("screenshots")]
    public bool Screenshots { get; set; }

    /// <summary>
    /// Attempts to load the replay plan from input_replay.json in the given directory.
    /// Returns null if the file doesn't exist or cannot be parsed.
    /// </summary>
    public static InputReplayPlan TryLoad(string debugDirectory)
    {
        var filePath = Path.Combine(debugDirectory, "input_replay.json");
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<InputReplayPlan>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public class InputReplayCommand
{
    [JsonPropertyName("action")]
    public string Action { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("time")]
    public float Time { get; set; }
}
