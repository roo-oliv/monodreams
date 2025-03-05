using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.InGameDebug;
using MonoDreams.Objects;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Draw;
using MonoDreams.System.Physics;

namespace MonoDreams.Examples.Screens;

public class DreamGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly Camera _camera;
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly LevelLoader _levelLoader;
    private readonly (RenderTarget2D main, RenderTarget2D ui) _renderTargets;
    
    public DreamGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ResolutionIndependentRenderer renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _content = content;
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = (
            main: new RenderTarget2D(graphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight),
            ui: new RenderTarget2D(graphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight)
        );
        
        camera.Position = new Vector2(0, 0);
        
        _world = new World();
        _levelLoader = new LevelLoader(_world, graphicsDevice, _content, _spriteBatch, _renderTargets);
        System = CreateSystem();
    }

    public ISystem<GameState> System { get; }
    public void Load(ScreenController screenController, ContentManager content)
    {
        _levelLoader.LoadLevel(0);
        InGameDebug.Create(_world, _spriteBatch, _renderer);
    }
    
    private SequentialSystem<GameState> CreateSystem()
    {
        return new SequentialSystem<GameState>(
            new DebugSystem(_world, _game, _spriteBatch),
            new InputHandlingSystem(),
            new MovementSystem(_world, _parallelRunner),
            // new GravitySystem(_world, _parallelRunner, Constants.WorldGravity, Constants.MaxFallVelocity),
            new VelocitySystem(_world, _parallelRunner),
            new CollisionDetectionSystem<CollisionMessage>(_world, _parallelRunner, CollisionMessage.Create),
            new PhysicalCollisionResolutionSystem(_world),
            new PositionSystem(_world, _parallelRunner),
            new BeginDrawSystem(_spriteBatch, _renderer, _camera),
            new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(_renderTargets.main)),
            new ParallelSystem<GameState>(_parallelRunner,
                new DrawSystem(_world, _spriteBatch, _renderTargets.main, _parallelRunner),
                new DynamicTextSystem(_spriteBatch, _world, _renderTargets.main)
                ),
            new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(_renderTargets.ui)),
            new ParallelSystem<GameState>(_parallelRunner,
                new DrawSystem(_world, _spriteBatch, _renderTargets.ui, _parallelRunner),
                new DynamicTextSystem(_spriteBatch, _world, _renderTargets.ui)
            ),
            new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(null)),
            new EndDrawSystem(_spriteBatch)
            // new DrawDebugSystem(_world, _spriteBatch, _renderer)
            );
    }
    
    public void Dispose()
    {
        System.Dispose();
        GC.SuppressFinalize(this);
    }
    
    public enum DrawLayer
    {
        Cursor,
        UIText,
        UIElements,
        Player,
        Level,
        Background,
    }
}
