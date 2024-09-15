using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.System;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Draw;
using MonoDreams.System.Physics.VelocityBased;

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
    
    public DreamGameScreen(Game game, ContentManager content, Camera camera, ResolutionIndependentRenderer renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _content = content;
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        
        camera.Position = new Vector2(440, 340);
        
        _world = new World();
        _levelLoader = new LevelLoader(_world, _content, _renderer);
        System = CreateSystem();
    }

    public ISystem<GameState> System { get; }
    public void Load(ScreenController screenController, ContentManager content)
    {
        _levelLoader.LoadLevel(0);
    }
    
    private SequentialSystem<GameState> CreateSystem()
    {
        return new SequentialSystem<GameState>(
            new InputHandlingSystem(),
            new MovementSystem(_world, _parallelRunner),
            new GravityUpdatesVelocitySystem(_world, _parallelRunner, Constants.MaxFallVelocity),
            new VelocityUpdatesPositionSystem(_world, _parallelRunner),
            new CollisionDetectionSystem(_world, _parallelRunner),
            new CollisionResolutionSystem(_world),
            new PositionUpdatesItselfSystem(_world, _parallelRunner),
            new VelocityUpdatesItselfSystem(_world, _parallelRunner),
            // new FloorSystem(World),
            // new WallSystem(World),
            new BeginDrawSystem(_spriteBatch, _renderer, _camera),
            new DrawSystem(_world, _spriteBatch, _parallelRunner),
            new EndDrawSystem(_spriteBatch)
            );
    }
    
    public void Dispose()
    {
        System.Dispose();
        GC.SuppressFinalize(this);
    }
}
