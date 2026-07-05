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
/// The <b>native-first level-load dispatcher</b> on <c>LoadLevelRequest</c>. It decides, per request,
/// whether a level id resolves to a <b>native MonoDreams scene</b> (a bundled <c>.mdscene</c>) or the
/// legacy <b>LDtk</b> content path, and — when native — short-circuits before the LDtk attempt so the
/// two paths never collide.
///
/// <para><b>Native-first (PS4).</b> When a native-scene loader is composed (the optional
/// <c>tryLoadNativeScene</c> delegate — built by <c>NativeLevelLoader.CreateProbe</c> in the
/// <c>level-editor</c> module, kept as a plain <see cref="Func{T,TResult}"/> here so <c>level-loading</c>
/// never depends upward on <c>level-editor</c>), each <see cref="LoadLevelRequest"/> is first probed for
/// <c>Content/Levels/&lt;id&gt;.mdscene</c> via <c>TitleContainer</c> (the console-portable read). If the
/// native scene exists, the delegate loads it (through the native reader) and returns <c>true</c>; this
/// system then <b>returns immediately</b> — it never runs the LDtk <c>Content.Load</c> and never removes
/// <see cref="CurrentLevelComponent"/>, so a native load is not clobbered by a failed LDtk attempt.</para>
///
/// <para><b>Fallback (migration coexistence — banked decision 4).</b> When no native loader is composed,
/// or the probe finds no <c>.mdscene</c> for the id, the behaviour below is <b>unchanged</b>: load the
/// LDtk level and set the <see cref="CurrentLevelComponent"/> singleton (which drives the LDtk tile +
/// entity parsers). The <c>Blender_</c>-prefixed path stays its own orthogonal subscriber
/// (<c>BlenderLevelParserSystem</c>); a native id never starts with <c>Blender_</c>, so the two do not
/// conflict. This dual fallback (LDtk + Blender) is removed in PS5 once the Examples levels are migrated
/// to native scenes; native-first is then the sole load path and the CORE_TENETS §6 parser-asymmetry
/// backlog closes.</para>
/// </summary>
public sealed class LevelLoadRequestSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly LDtkWorld _ldtkWorld;
    private readonly Func<string, bool>? _tryLoadNativeScene;

    /// <summary>
    /// The native-first level-load dispatcher on <c>LoadLevelRequest</c> (see the type doc).
    /// </summary>
    /// <param name="tryLoadNativeScene">
    /// Optional native-first hook: given a level id, returns <c>true</c> if a native <c>.mdscene</c>
    /// existed and was loaded (in which case the LDtk path is skipped), or <c>false</c> to fall through
    /// to the LDtk path. Build it with <c>NativeLevelLoader.CreateProbe</c> (level-editor). When
    /// <c>null</c> (a game with no native support composed), behaviour is the legacy LDtk path.
    /// </param>
    public LevelLoadRequestSystem(World world, ContentManager content,
        Func<string, bool>? tryLoadNativeScene = null)
    {
        _world = world;
        _content = content;
        _tryLoadNativeScene = tryLoadNativeScene;
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
        // we skip the LDtk path entirely (no Content.Load, no CurrentLevelComponent removal). This is
        // the unified load entry (PS4) — probe native BEFORE the LDtk attempt below.
        if (_tryLoadNativeScene != null && _tryLoadNativeScene(levelIdentifier))
        {
            Logger.Info($"Level '{levelIdentifier}' resolved to a native .mdscene; loaded via the native reader (LDtk path skipped).");
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