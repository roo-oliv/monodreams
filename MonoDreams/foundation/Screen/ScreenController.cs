using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Screen;

/// <summary>
/// The editor-facing metadata a screen declares at registration (foundation seam, UX-C): a
/// human-readable <paramref name="DisplayName"/>, the scene id the screen loads from
/// (<paramref name="BoundSceneId"/>, null when the screen is not tied to one scene file), and whether
/// the screen is the level-parameterized <b>host</b> that loads whatever scene is requested
/// (<paramref name="HostsSceneFiles"/>). The editor's Scenes panel reads these — code is the source of
/// truth for which configuration file a screen loads from — but this record is pure data with no
/// dependency on the editor, so registering info never pulls the editor into a plain game.
/// </summary>
public sealed record ScreenInfo(string DisplayName, string? BoundSceneId = null, bool HostsSceneFiles = false);

public class ScreenController(
    Game game,
    IParallelRunner runner,
    ViewportManager renderer,
    Camera camera,
    SpriteBatch spriteBatch,
    ContentManager content)
    : IDisposable
{
    public Game Game { get; } = game;
    public IParallelRunner Runner { get; } = runner;
    public ViewportManager Renderer { get; } = renderer;
    public Camera Camera { get; } = camera;
    public SpriteBatch SpriteBatch { get; } = spriteBatch;
    public ContentManager Content { get; } = content;

    private (IGameScreen current, IGameScreen next) _screen;
    private readonly GameState _state = new(new GameTime());

    /// <summary>
    /// The single <see cref="GameState"/> every screen's pipelines run against. Exposed so the
    /// host can apply boot-time run configuration after construction — e.g. the editor run flag
    /// sets <c>State.RunMode = RunMode.Edit</c> so the game boots straight into editing. The
    /// constructed default stays <see cref="RunMode.Play"/> (the back-compat premise); any
    /// deviation is an explicit host-level opt-in through this property.
    /// </summary>
    public GameState State => _state;

    public World CurrentWorld => _screen.current?.World;

    private readonly Dictionary<string, Func<IGameScreen>> _screenCreators = new();
    // Registration-order list of (name, info) so the editor's Scenes panel enumerates screens in the
    // order the host declared them (Dictionary enumeration order is not part of its contract).
    private readonly List<(string Name, ScreenInfo Info)> _registered = new();

    /// <summary>Registers a screen with default <see cref="ScreenInfo"/> (display name = the screen
    /// name, no bound scene, not a scene host). The additive overload is the pre-UX-C behaviour.</summary>
    public void RegisterScreen(string screenName, Func<IGameScreen> creator) =>
        RegisterScreen(screenName, creator, new ScreenInfo(screenName));

    /// <summary>Registers a screen with explicit editor-facing <see cref="ScreenInfo"/> (display name,
    /// bound scene id, host flag). Duplicate names throw, exactly as the default overload.</summary>
    public void RegisterScreen(string screenName, Func<IGameScreen> creator, ScreenInfo info)
    {
        if (!_screenCreators.TryAdd(screenName, creator))
        {
            throw new ArgumentException($"Screen '{screenName}' is already registered.");
        }
        _registered.Add((screenName, info));
    }

    /// <summary>The registered screens with their <see cref="ScreenInfo"/>, in registration order —
    /// the editor's Scenes panel reads this to list screen-bound scenes (UX-C). Read-only.</summary>
    public IReadOnlyList<(string Name, ScreenInfo Info)> RegisteredScreens => _registered;

    public void Update(GameTime gameTime)
    {
        if (_screen.next != null)
        {
            _screen.current?.Dispose();
            _screen.current = _screen.next;
            _screen.next = null;
            _screen.current.Load(this, Content);
        }

        _state.Update(gameTime);
        Logger.UpdateGameTime(_state.TotalTime);
        _screen.current?.UpdateSystem.Update(_state);
    }
    
    public void Draw(GameTime gameTime)
    {
        _screen.current?.DrawSystem.Update(_state);
    }

    public void LoadScreen(string screenName)
    {
        if (_screenCreators.TryGetValue(screenName, out var creator))
        {
            _screen.next = creator();
        }
        else
        {
            throw new ArgumentException($"Screen '{screenName}' is not registered.");
        }
    }

    public void Dispose()
    {
        _screen.current?.Dispose();
        GC.SuppressFinalize(this);
    }
}
