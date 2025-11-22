namespace MonoDreams.Examples.Component;

/// <summary>
/// Singleton component that stores the level identifier requested for loading.
/// Used to pass level selection data between screens.
/// </summary>
public class RequestedLevelComponent(string levelIdentifier)
{
    public string LevelIdentifier { get; } = levelIdentifier;
}
