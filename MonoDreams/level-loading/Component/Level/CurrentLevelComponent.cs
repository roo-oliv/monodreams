namespace MonoDreams.Component.Level;

/// <summary>
/// Singleton marker for the currently-loaded level, keyed by identifier. Set when a level becomes
/// active; removed on teardown/restart (see the editor transport). Was previously LDtk-typed
/// (<c>LDtkLevel</c>); decoupled to a plain identifier now that the boot path is native-only
/// (<c>.mdscene</c> via the native reader) and the LDtk parser is off the boot path.
///
/// <para>Use <c>world.Set(new CurrentLevelComponent(levelIdentifier))</c> to set and
/// <c>world.Get&lt;CurrentLevelComponent&gt;()</c> to access. The full parsed LDtk level (when a game
/// opts into LDtk) lives in the <c>level-ldtk</c>-local <c>LDtkLevelDataComponent</c> instead, so
/// <c>level-loading</c> never compiles against LDtk.</para>
/// </summary>
public readonly struct CurrentLevelComponent(string levelIdentifier)
{
    public readonly string LevelIdentifier = levelIdentifier;
}
