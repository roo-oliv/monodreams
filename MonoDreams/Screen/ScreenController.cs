using System;
using System.Collections.Generic;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Screen;

public class ScreenController(
    Game game,
    IParallelRunner runner,
    ResolutionIndependentRenderer renderer,
    Camera camera,
    SpriteBatch spriteBatch,
    ContentManager content)
    : IDisposable
{
    public Game Game { get; } = game;
    public IParallelRunner Runner { get; } = runner;
    public ResolutionIndependentRenderer Renderer { get; } = renderer;
    public Camera Camera { get; } = camera;
    public SpriteBatch SpriteBatch { get; } = spriteBatch;
    public ContentManager Content { get; } = content;

    private (IGameScreen current, IGameScreen next) _screen;
    private GameState _state = new(new GameTime());

    private readonly Dictionary<string, Func<IGameScreen>> _screenCreators = new();

    public void RegisterScreen(string screenName, Func<IGameScreen> creator)
    {
        if (!_screenCreators.TryAdd(screenName, creator))
        {
            throw new ArgumentException($"Screen '{screenName}' is already registered.");
        }
    }

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
        _screen.current?.System.Update(_state);
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
