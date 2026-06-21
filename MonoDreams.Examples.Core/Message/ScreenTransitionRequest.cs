namespace MonoDreams.Examples.Message;

/// <summary>
/// Message published to request a screen transition.
/// </summary>
public readonly struct ScreenTransitionRequest(string screenName, string? levelIdentifier = null)
{
    /// <summary>
    /// The name of the screen to transition to.
    /// </summary>
    public readonly string ScreenName = screenName;

    /// <summary>
    /// Optional level identifier to load when transitioning.
    /// </summary>
    public readonly string? LevelIdentifier = levelIdentifier;
}
