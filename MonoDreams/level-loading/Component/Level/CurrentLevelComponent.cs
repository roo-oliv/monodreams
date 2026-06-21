using LDtk;

namespace MonoDreams.Component.Level;

/// <summary>
/// Singleton component holding the data for the currently loaded LDtk level.
/// Use world.Set(new CurrentLevelComponent(levelData)) to set
/// and world.Get<CurrentLevelComponent>() to access.
/// </summary>
public readonly struct CurrentLevelComponent(LDtkLevel levelData)
{
    public readonly LDtkLevel LevelData = levelData;
}