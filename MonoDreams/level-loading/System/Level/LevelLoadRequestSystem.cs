using System;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Message.Level;
using MonoDreams.State;

namespace MonoDreams.System.Level;

/// <summary>
/// The <b>native-only level-load dispatcher</b> on <c>LoadLevelRequest</c>. A level id resolves to a
/// bundled native MonoDreams scene (<c>Content/Levels/&lt;id&gt;.mdscene</c>) loaded through the native
/// reader via the <c>tryLoadNativeScene</c> probe. An id with <b>no</b> native scene <b>fails loud</b>
/// (a logged error, no entities). This is the single content-driven load path that closes the
/// CORE_TENETS §6/§10 parser-asymmetry.
///
/// <para><b>Native (PS4).</b> Each <see cref="LoadLevelRequest"/> is probed for
/// <c>Content/Levels/&lt;id&gt;.mdscene</c> via <c>TitleContainer</c> (the console-portable read) by the
/// optional probe delegate — built by <c>NativeLevelLoader.CreateProbe</c> in the <c>level-editor</c>
/// module, kept as a plain <see cref="Func{T,TResult}"/> here so <c>level-loading</c> never depends
/// upward on <c>level-editor</c>. On a hit the delegate loads the scene through the native reader and
/// returns <c>true</c>; this system then returns.</para>
///
/// <para><b>No LDtk.</b> <c>level-loading</c> is decoupled from LDtk (issue #54): there is no legacy
/// <c>Content.Load</c> boot path here and this module does not compile against LDtk at all. The LDtk
/// parser survives as <b>import-only</b> machinery in <c>level-ldtk</c> — its own
/// <c>LDtkLevelLoadSystem</c> is composed solely by the import op (the reference screen's
/// <c>importMode</c>), never at live game boot.</para>
/// </summary>
public sealed class LevelLoadRequestSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly Func<string, bool>? _tryLoadNativeScene;

    /// <summary>
    /// The native-only level-load dispatcher on <c>LoadLevelRequest</c> (see the type doc).
    /// </summary>
    /// <param name="tryLoadNativeScene">
    /// Native-first hook: given a level id, returns <c>true</c> if a native <c>.mdscene</c> existed and was
    /// loaded (nothing else runs), or <c>false</c> otherwise. Build it with
    /// <c>NativeLevelLoader.CreateProbe</c> (level-editor). When null, every request fails loud.
    /// </param>
    public LevelLoadRequestSystem(World world, Func<string, bool>? tryLoadNativeScene = null)
    {
        _world = world;
        _tryLoadNativeScene = tryLoadNativeScene;
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

        // No native scene ⇒ fail loud (there is no LDtk boot path).
        Logger.Error(
            $"No native scene 'Content/Levels/{levelIdentifier}.mdscene' found for level " +
            $"'{levelIdentifier}'. The level was not loaded — author or migrate it to a native .mdscene.");
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
