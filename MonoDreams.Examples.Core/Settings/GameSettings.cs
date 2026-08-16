using System.Text.Json;
using System.Text.Json.Serialization;
using MonoDreams.Platform;

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
    /// RENDER resolution width — the pixel size of the render targets and back buffer.
    /// </summary>
    public int VirtualWidth { get; set; } = 1920;

    /// <summary>
    /// RENDER resolution height.
    /// </summary>
    public int VirtualHeight { get; set; } = 1080;

    /// <summary>
    /// AUTHORING (layout) resolution width — the space every game coordinate is written in.
    /// 0 (the default) means "same as <see cref="VirtualWidth"/>": the single-space game, where the
    /// two-space model is inert. Set it to author at a fixed canvas (say 1280x720) while rendering
    /// at a higher resolution — the aspect ratios must match, and NO game coordinate, UI number or
    /// coordinate-carrying test moves when <see cref="VirtualWidth"/> does.
    /// </summary>
    public int LayoutWidth { get; set; }

    /// <summary>
    /// AUTHORING (layout) resolution height; 0 means "same as <see cref="VirtualHeight"/>".
    /// </summary>
    public int LayoutHeight { get; set; }

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
    public string ScalingMode { get; set; } = "KeepAspectRatio";

    /// <summary>
    /// When true, sprite and text positions are snapped to integer coordinates at render time.
    /// Physics and logic retain sub-pixel precision. Recommended for pixel art games.
    /// </summary>
    public bool PixelPerfectRendering { get; set; } = false;

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
        PlatformServices.Current.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads settings from a JSON file. Returns default settings if file doesn't exist.
    /// </summary>
    public static GameSettings Load(string path)
    {
        if (!PlatformServices.Current.FileExists(path))
        {
            var defaultSettings = new GameSettings();
            defaultSettings.Save(path);
            return defaultSettings;
        }

        try
        {
            var json = PlatformServices.Current.ReadAllText(path);
            return JsonSerializer.Deserialize<GameSettings>(json, JsonOptions) ?? new GameSettings();
        }
        catch
        {
            return new GameSettings();
        }
    }
}
