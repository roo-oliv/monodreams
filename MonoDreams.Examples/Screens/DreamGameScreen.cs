using DefaultEcs.System;
using DefaultEcs.Threading;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.System;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using DefaultEcsWorld = DefaultEcs.World;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.Screens;

public class DreamGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly Camera _camera;
    private readonly ViewportManager _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly DefaultEcsWorld _defaultEcsWorld;
    private World _world;
    private readonly LevelLoader _levelLoader;
    private readonly (RenderTarget2D main, RenderTarget2D ui) _renderTargets;
    
    public DreamGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _content = content;
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = (
            main: new RenderTarget2D(graphicsDevice, _renderer.VirtualWidth, _renderer.VirtualHeight),
            ui: new RenderTarget2D(graphicsDevice, _renderer.VirtualWidth, _renderer.VirtualHeight)
        );
        
        // Initialize camera for the larger virtual space
        camera.Position = new Vector2(_renderer.VirtualWidth * 0.5f, _renderer.VirtualHeight * 0.5f);
        camera.Zoom = 1.0f; // Start at 1:1 pixel ratio
        
        _defaultEcsWorld = new DefaultEcsWorld();
        _levelLoader = new LevelLoader(_defaultEcsWorld, graphicsDevice, _content, _spriteBatch, _renderTargets);
        UpdateSystem = CreateSystem();
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public void Update(GameTime gameTime)
    {
        throw new NotImplementedException();
    }

    public void Draw(GameTime gameTime)
    {
        throw new NotImplementedException();
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _levelLoader.LoadLevel(0);
    }
    
    private SequentialSystem<GameState> CreateSystem()
    {
        return new SequentialSystem<GameState>();
        // return new SequentialSystem<GameState>(
        //     new InputMappingSystem(_world),
        //     new MovementSystem(_world, _parallelRunner),
        //     // new GravitySystem(_world, _parallelRunner, Constants.WorldGravity, Constants.MaxFallVelocity),
        //     new VelocitySystem(_world, _parallelRunner),
        //     new CollisionDetectionSystem<CollisionMessage>(_world, _parallelRunner, CollisionMessage.Create),
        //     new DialogueContentSystem(_world),
        //     // new DialogueTriggerSystem(_world, player),
        //     // new DialogueStateSystem(_world),
        //     // new DialogueInputSystem(_world),
        //     // new DialoguePresentationSystem(_world, _spriteBatch.GraphicsDevice),
        //     new PhysicalCollisionResolutionSystem(_world),
        //     new PositionSystem(_world, _parallelRunner),
        //     new BeginDrawSystem(_spriteBatch, _camera),
        //     new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(_renderTargets.main)),
        //     new ParallelSystem<GameState>(_parallelRunner,
        //         new DrawSystem(_world, _spriteBatch, _renderTargets.main, _parallelRunner),
        //         new DynamicTextSystem(_spriteBatch, _world, _renderTargets.main)
        //         ),
        //     new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(_renderTargets.ui)),
        //     new ParallelSystem<GameState>(_parallelRunner,
        //         new DrawSystem(_world, _spriteBatch, _renderTargets.ui, _parallelRunner),
        //         new DynamicTextSystem(_spriteBatch, _world, _renderTargets.ui)
        //     ),
        //     new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(null)),
        //     new EndDrawSystem(_spriteBatch)
        //     // new DrawDebugSystem(_world, _spriteBatch, _renderer)
        //     );
    }
    
    private Pipeline CreateUpdatePipeline()
    {
        return _world.Pipeline()
            .With(Ecs.System)
            .Without(DrawPhase)
            .Build();
    }
    
    private Pipeline CreateDrawPipeline()
    {
        return _world.Pipeline()
            .With(Ecs.System)
            .With(DrawPhase)
            .Build();
    }
    
    public void Dispose()
    {
        UpdateSystem.Dispose();
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
