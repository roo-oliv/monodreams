namespace MonoDreams.Examples.Settings;

/// <summary>
/// Singleton manager for game settings. Handles loading, saving, and providing access to settings.
/// </summary>
public class SettingsManager
{
    private const string DefaultSettingsFileName = "settings.json";

    private static SettingsManager? _instance;
    private static readonly object Lock = new();

    private readonly string _settingsPath;
    private GameSettings _settings;

    /// <summary>
    /// Gets the singleton instance of SettingsManager.
    /// </summary>
    public static SettingsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (Lock)
                {
                    _instance ??= new SettingsManager();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Gets the current game settings.
    /// </summary>
    public GameSettings Settings => _settings;

    private SettingsManager() : this(DefaultSettingsFileName)
    {
    }

    private SettingsManager(string settingsFileName)
    {
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName);
        _settings = GameSettings.Load(_settingsPath);
    }

    /// <summary>
    /// Initializes the settings manager with a custom settings file path.
    /// Call this before accessing Instance if you want a custom path.
    /// </summary>
    public static void Initialize(string settingsPath)
    {
        lock (Lock)
        {
            if (_instance != null)
            {
                throw new InvalidOperationException("SettingsManager has already been initialized.");
            }
            _instance = new SettingsManager(settingsPath);
        }
    }

    /// <summary>
    /// Saves the current settings to disk.
    /// </summary>
    public void Save()
    {
        _settings.Save(_settingsPath);
    }

    /// <summary>
    /// Reloads settings from disk.
    /// </summary>
    public void Reload()
    {
        _settings = GameSettings.Load(_settingsPath);
    }

    /// <summary>
    /// Resets settings to defaults and saves.
    /// </summary>
    public void ResetToDefaults()
    {
        _settings = new GameSettings();
        Save();
    }
}
