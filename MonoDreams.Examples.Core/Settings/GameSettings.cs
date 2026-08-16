using System.Text.Json;
using System.Text.Json.Serialization;
using MonoDreams.Platform;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Settings;

/// <summary>
/// Game settings that can be saved and loaded from a JSON file.
/// </summary>
public class GameSettings
{
    /// <summary>
    /// Whether to run in fullscreen mode.
    /// </summary>
    /// <remarks>
    /// There is deliberately NO window width/height here any more (issue #115). A windowed desktop
    /// run is sized by <c>WindowFit.Apply</c> from the display's usable area — the engine's single
    /// owner of "how big is this game's window" (foundation premise "<c>WindowFit</c> is opt-in, and
    /// it is the ONLY thing allowed to size a game's window") — with <c>MONODREAMS_WINDOW=WxH</c> as
    /// the explicit override for scripted runs. A second window-size dial in this file could only
    /// disagree with it: the pinned 1920x1080 that used to live here is exactly what rendered the
    /// bottom of the menu below the physical screen on a smaller laptop.
    /// </remarks>
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
    /// The presentation scaling policy — how the window-vs-render-resolution conflict is resolved:
    /// <c>Default</c> (overscan to a clean scale within 5% → letter/pillarbox at a clean scale
    /// within 25% → stretch — the recommended policy for a new game), <c>Crisp</c> (never resample
    /// at a fractional ratio, however wide the bars get), <c>PixelPerfect</c> (whole scales only,
    /// centered, with bars) or <c>Stretch</c> (the historical aspect-fit present). Whichever wins,
    /// every layer still samples point at an integer scale and linear otherwise.
    /// </summary>
    public string Presentation { get; set; } = "Default";

    /// <summary>
    /// <see cref="Presentation"/> as the policy object to hand
    /// <c>ViewportManager.Policy</c> — a method, not a property, so it never lands in the
    /// serialized settings file. Both heads call it, so desktop and web present the same way from
    /// the same settings; an unknown name falls back to the recommended policy rather than to the
    /// historical stretch, so a typo cannot silently downgrade the framing.
    /// </summary>
    public PresentationPolicy ResolvePresentation() => Presentation switch
    {
        "PixelPerfect" => PresentationPolicy.PixelPerfect,
        "Crisp" => PresentationPolicy.Crisp,
        "Stretch" => PresentationPolicy.Stretch,
        _ => PresentationPolicy.Default,
    };

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
