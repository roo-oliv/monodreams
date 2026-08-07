namespace MonoDreams.Message.Level;

/// <summary>
/// Message published to request loading a specific level. Exactly one load dispatcher
/// should be composed to handle it: <c>LevelLoadRequestSystem</c> for the native
/// <c>.mdscene</c> boot, or a format module's own loader (<c>level-ldtk</c>'s
/// <c>LDtkLevelLoadSystem</c>) in an import pipeline.
/// </summary>
public readonly struct LoadLevelRequest(string levelIdentifier)
{
    /// <summary>
    /// The identifier (name) of the level to load — format-agnostic: the native scene id
    /// under <c>Content/Levels/&lt;id&gt;.mdscene</c> for the shipped boot, or the level's
    /// name in the source format for an import loader.
    /// </summary>
    public readonly string LevelIdentifier = levelIdentifier;
}