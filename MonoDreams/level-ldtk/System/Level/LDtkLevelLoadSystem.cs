using System;
using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Component.Level;
using MonoDreams.Message.Level;
using MonoDreams.State;

namespace MonoDreams.System.Level;

/// <summary>
/// The <b>import-only LDtk level loader</b> on <c>LoadLevelRequest</c>: it loads the legacy
/// <c>Content.Load&lt;LDtkLevel&gt;("World/&lt;id&gt;")</c> asset and publishes the parsed level into the
/// world as <see cref="LDtkLevelDataComponent"/> (plus the plain-string <see cref="CurrentLevelComponent"/>
/// and <see cref="CurrentBackgroundColorComponent"/>), which is what triggers the component-driven LDtk
/// parsers.
///
/// <para><b>Import op only.</b> This system is composed <b>solely</b> in the import op (the reference
/// screen's <c>importMode</c> — the headless <c>--export-scene &lt;id&gt;</c> /
/// <c>MONODREAMS_EXPORT_SCENE</c> dev op, or a future editor toolbar action), which re-parses a legacy
/// level so the importer can serialize it to a native <c>.mdscene</c> the game then owns. It is
/// <b>never</b> wired to live game boot: the shipped boot is <c>LevelLoadRequestSystem</c>'s native-only
/// dispatch, and an unmigrated id fails loud there (CORE_TENETS §6).</para>
///
/// <para>This is what replaced <c>LevelLoadRequestSystem</c>'s deleted <c>enableLegacyLdtkFallback</c>
/// path (issue #54). Moving the LDtk load here is what lets <c>level-loading</c> drop its LDtk
/// dependency entirely: the arrow now points <c>level-ldtk → level-loading</c> and never back.</para>
/// </summary>
public sealed class LDtkLevelLoadSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ContentManager _content;

    /// <summary>
    /// The import-only LDtk loader on <c>LoadLevelRequest</c> (see the type doc). Compose it
    /// <b>instead of</b> <c>LevelLoadRequestSystem</c>'s native probe on the import path.
    /// </summary>
    public LDtkLevelLoadSystem(World world, ContentManager content)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _world.Subscribe<LoadLevelRequest>(On);
    }

    public bool IsEnabled { get; set; } = true;

    [Subscribe]
    public void On(in LoadLevelRequest message)
    {
        if (!IsEnabled) return;

        var levelIdentifier = message.LevelIdentifier;
        Logger.Info($"Received request to import legacy LDtk level '{levelIdentifier}'.");

        try
        {
            var levelData = _content.Load<LDtkLevel>($"World/{levelIdentifier}");

            if (levelData != null)
            {
                Logger.Info($"Found LDtk level data for '{levelIdentifier}'. Setting LDtkLevelDataComponent.");

                _world.Set(new LDtkLevelDataComponent(levelData));
                _world.Set(new CurrentLevelComponent(levelIdentifier));
                _world.Set(new CurrentBackgroundColorComponent(levelData._BgColor));
            }
            else
            {
                Logger.Error($"Failed to find LDtk level data for '{levelIdentifier}'.");
                _world.Remove<LDtkLevelDataComponent>();
                _world.Remove<CurrentLevelComponent>();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Error during LDtk level import request handling for level '{levelIdentifier}':" +
                $"\n-----\n{ex.Message}\n{ex.StackTrace}\n-----");
            _world.Remove<LDtkLevelDataComponent>();
            _world.Remove<CurrentLevelComponent>();
            _world.Remove<CurrentBackgroundColorComponent>();
        }
    }

    public void Update(GameState state)
    {
        // Message handling is done via subscription.
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
