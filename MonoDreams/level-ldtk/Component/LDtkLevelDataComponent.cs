using LDtk;

namespace MonoDreams.Component.Level;

/// <summary>
/// LDtk-local world singleton holding the <b>full parsed <see cref="LDtkLevel"/></b> — the complete LDtk
/// richness (layer instances, grid tiles, entity instances, field instances) for games that opt into
/// LDtk. It lives in <c>level-ldtk</c> precisely so <c>level-loading</c> stays LDtk-free (issue #54): a
/// game that never installs this module never compiles against the LDtk types.
///
/// <para>Set by <c>LDtkLevelLoadSystem</c> on the <b>import path</b> (alongside the plain-string
/// <c>CurrentLevelComponent</c>), and it is the component the LDtk parsers
/// (<c>LDtkTileParserSystem</c>, <c>LDtkEntityParserSystem</c>) subscribe to being <b>added</b> — the
/// engine-wide component-driven convention, so a test or tool that sets this component manually
/// triggers parsing exactly as a <c>LoadLevelRequest</c> would.</para>
/// </summary>
public readonly struct LDtkLevelDataComponent(LDtkLevel levelData)
{
    public readonly LDtkLevel LevelData = levelData;
}
