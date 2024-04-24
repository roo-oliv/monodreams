using System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Screen;

public class ScreenController(
    IParallelRunner runner,
    ResolutionIndependentRenderer renderer,
    Camera camera,
    SpriteBatch spriteBatch,
    ContentManager content)
    : IDisposable
{
    public IParallelRunner Runner { get; } = runner;
    public ResolutionIndependentRenderer Renderer { get; } = renderer;
    public Camera Camera { get; } = camera;
    public SpriteBatch SpriteBatch { get; } = spriteBatch;
    public ContentManager Content { get; } = content;

    private (IGameScreen current, IGameScreen next) _screen;
    private GameState _state = new(new GameTime());

    public void Update(GameTime gameTime)
    {
        _state.Update(gameTime);
        _screen.current.System.Update(_state);
    }

    public void LoadScreen(IGameScreen screen)
    {
        if (_screen.current is null)
        {
            _screen.current = screen;
            _screen.current.Load(Content);
            return;
        }
        
        _screen.next = screen;
    }

    public void Dispose()
    {
        _screen.current?.Dispose();
        _screen.next?.Dispose();
        GC.SuppressFinalize(this);
    }
}
