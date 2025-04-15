namespace MonoDreams.Examples.Message.Level;

/// <summary>
/// Message published to request loading a specific level.
/// </summary>
public readonly struct LoadLevelRequest(string levelIdentifier)
{
    /// <summary>
    /// The identifier (name) of the level to load, as defined in LDtk.
    /// </summary>
    public readonly string LevelIdentifier = levelIdentifier;
}