using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.Examples.Settings;

/// <summary>
/// Game settings that can be saved and loaded from a JSON file.
/// </summary>
public class GameSettings
{
    /// <summary>
    /// Window width in pixels.
    /// </summary>
    public int WindowWidth { get; set; } = 1920;

    /// <summary>
    /// Window height in pixels.
    /// </summary>
    public int WindowHeight { get; set; } = 1080;

    /// <summary>
    /// Whether to run in fullscreen mode.
    /// </summary>
    public bool IsFullscreen { get; set; } = false;

    /// <summary>
    /// Virtual resolution width (game canvas).
    /// </summary>
    public int VirtualWidth { get; set; } = 1920;

    /// <summary>
    /// Virtual resolution height (game canvas).
    /// </summary>
    public int VirtualHeight { get; set; } = 1080;

    /// <summary>
    /// Camera zoom level. 1.0 = 1:1 view of virtual canvas.
    /// </summary>
    public float CameraZoom { get; set; } = 1.0f;

    /// <summary>
    /// Resolution scaling mode: PixelPerfect, Smooth, or KeepAspectRatio.
    /// PixelPerfect uses integer scaling for crisp pixel art.
    /// Smooth uses bilinear filtering for UI/text.
    /// KeepAspectRatio maintains aspect ratio with fractional scaling.
    /// </summary>
    public string ScalingMode { get; set; } = "Smooth";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Saves settings to a JSON file.
    /// </summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads settings from a JSON file. Returns default settings if file doesn't exist.
    /// </summary>
    public static GameSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaultSettings = new GameSettings();
            defaultSettings.Save(path);
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GameSettings>(json, JsonOptions) ?? new GameSettings();
        }
        catch
        {
            return new GameSettings();
        }
    }
}
