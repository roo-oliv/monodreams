using System;
using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Component.Level;
using MonoDreams.Message.Level;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System.Level;

/// <summary>
/// The <b>native-first level-load dispatcher</b> on <c>LoadLevelRequest</c>. For the shipped game it is
/// <b>native-only</b> (PS5): a level id resolves to a bundled native MonoDreams scene
/// (<c>Content/Levels/&lt;id&gt;.mdscene</c>) loaded through the native reader, and there is <b>no</b>
/// legacy LDtk boot loader — that parser is now import-only machinery (it runs once, via the
/// import op, to produce a <c>.mdscene</c>), never wired to live game boot. This closes the CORE_TENETS
/// §6/§10 parser-asymmetry: one content-driven load path.
///
/// <para><b>Native (PS4).</b> Each <see cref="LoadLevelRequest"/> is probed for
/// <c>Content/Levels/&lt;id&gt;.mdscene</c> via <c>TitleContainer</c> (the console-portable read) by the
/// optional <c>tryLoadNativeScene</c> delegate — built by <c>NativeLevelLoader.CreateProbe</c> in the
/// <c>level-editor</c> module, kept as a plain <see cref="Func{T,TResult}"/> here so <c>level-loading</c>
/// never depends upward on <c>level-editor</c>. On a hit the delegate loads the scene through the native
/// reader and returns <c>true</c>; this system then returns. A truly-unknown id (no native scene) <b>fails
/// loud</b> — no silent LDtk attempt.</para>
///
/// <para><b>Legacy fallback = import-only opt-in.</b> The old LDtk <c>Content.Load</c> path survives
/// <b>only</b> when a caller explicitly opts in via <c>enableLegacyLdtkFallback</c> (the import op's
/// dedicated composition, which re-parses a legacy level so the importer can capture and serialize it).
/// The normal game/editor boot passes <c>false</c>, so the shipped game never touches the LDtk content.</para>
/// </summary>
public sealed class LevelLoadRequestSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly LDtkWorld _ldtkWorld;
    private readonly Func<string, bool>? _tryLoadNativeScene;
    private readonly bool _enableLegacyLdtkFallback;

    /// <summary>
    /// The native-first level-load dispatcher on <c>LoadLevelRequest</c> (see the type doc).
    /// </summary>
    /// <param name="tryLoadNativeScene">
    /// Optional native-first hook: given a level id, returns <c>true</c> if a native <c>.mdscene</c>
    /// existed and was loaded (in which case nothing else runs), or <c>false</c> otherwise. Build it
    /// with <c>NativeLevelLoader.CreateProbe</c> (level-editor).
    /// </param>
    /// <param name="enableLegacyLdtkFallback">
    /// When <c>true</c>, a request with no native scene falls back to the legacy LDtk content load (the
    /// <b>import-only</b> path — used solely by the import op to re-parse a legacy level). Defaults to
    /// <c>false</c>: the shipped game/editor boot is native-only and an unknown id fails loud.
    /// </param>
    public LevelLoadRequestSystem(World world, ContentManager content,
        Func<string, bool>? tryLoadNativeScene = null, bool enableLegacyLdtkFallback = false)
    {
        _world = world;
        _content = content;
        _tryLoadNativeScene = tryLoadNativeScene;
        _enableLegacyLdtkFallback = enableLegacyLdtkFallback;
        // Load the LDtk world only for the import-only path; the native-only game boot never touches it.
        if (_enableLegacyLdtkFallback)
            _ldtkWorld = _content.Load<LDtkFile>("World").LoadSingleWorld();
        _world.Subscribe<LoadLevelRequest>(On);
    }

    public bool IsEnabled { get; set; } = true;

    [Subscribe]
    public void On(in LoadLevelRequest message)
    {
        if (!IsEnabled) return;

        var levelIdentifier = message.LevelIdentifier;
        Logger.Info($"Received request to activate level '{levelIdentifier}'.");

        // Native-first: if a bundled Content/Levels/<id>.mdscene exists, the native reader loads it and
        // we return. This is the unified load entry (PS4/PS5).
        if (_tryLoadNativeScene != null && _tryLoadNativeScene(levelIdentifier))
        {
            Logger.Info($"Level '{levelIdentifier}' resolved to a native .mdscene; loaded via the native reader.");
            return;
        }

        // Native-only game boot (PS5): no native scene ⇒ fail loud. The LDtk loader is
        // import-only and not wired here, so there is no silent legacy attempt.
        if (!_enableLegacyLdtkFallback)
        {
            Logger.Error(
                $"No native scene 'Content/Levels/{levelIdentifier}.mdscene' found for level " +
                $"'{levelIdentifier}', and the legacy LDtk loader is import-only (not wired to " +
                "game boot). The level was not loaded. Migrate it to a native .mdscene (the import op).");
            return;
        }

        try
        {
            // Get the specific level data from the loaded world
            // var levelData = _ldtkWorld.Levels?.FirstOrDefault(l => l.Identifier == levelIdentifier);
            var levelData = _content.Load<LDtkLevel>($"World/{levelIdentifier}");
            
            if (levelData != null)
            {
                Logger.Info($"Found level data for '{levelIdentifier}'. Setting CurrentLevelComponent.");

                _world.Set(new CurrentLevelComponent(levelData));
                _world.Set(new CurrentBackgroundColorComponent(levelData._BgColor));

                // Optional: Publish a success message if other systems need to react immediately AFTER activation
                // _world.Publish(new LevelLoadSuccessMessage(levelData));
            }
            else
            {
                Logger.Error($"Failed to find level data for '{levelIdentifier}' in the loaded LDtkWorld.");
                // Potentially publish a LevelLoadFailed message?
                // Maybe clear the CurrentLevelComponent if loading fails?
                 _world.Remove<CurrentLevelComponent>();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Error during level activation request handling for level '{levelIdentifier}':" +
                $"\n-----\n{ex.Message}\n{ex.StackTrace}\n-----");
             // Potentially publish a LevelLoadFailed message?
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