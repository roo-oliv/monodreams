using DefaultEcs.System;
using DefaultEcs.Threading;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Camera;
using MonoDreams.Examples.System.Cursor;
using MonoDreams.Examples.System.Debug;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Examples.System.Level;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Camera = MonoDreams.Component.Camera;
using DefaultEcsWorld = DefaultEcs.World;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.Screens;

public class LoadLevelExampleGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GameState _gameState;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly DefaultEcsWorld _defaultEcsWorld;
    private World _world;
    private Pipeline _updatePipeline;
    private Pipeline _drawPipeline;
    private readonly LevelLoader _levelLoader;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    
    public LoadLevelExampleGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _gameState = new GameState(new GameTime());
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight) }
        };
        
        camera.Position = new Vector2(0, 0);
        
        _world = World.Create();
        _defaultEcsWorld = new DefaultEcsWorld();
        
        RegisterSystems();
        _updatePipeline = CreateUpdatePipeline();
        _drawPipeline = CreateDrawPipeline();
        
        // _levelLoader = new LevelLoader(_world, graphicsDevice, _content, _spriteBatch, _renderTargets);
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    private void RegisterSystems()
    {
        InputMappingSystem.Register(_world, _defaultEcsWorld, _gameState);
        MovementSystem.Register(_world, _gameState);
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }

    public void Update(GameTime gameTime)
    {
        _gameState.Update(gameTime);
        _world.RunPipeline(_updatePipeline, (float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public void Draw(GameTime gameTime)
    {
        _world.RunPipeline(_drawPipeline, (float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
            // Add more cursor types as needed
        };

        // Create cursor entity
        Objects.Cursor.Create(_defaultEcsWorld, cursorTextures, RenderTargetID.Main);
        _world.Set(new InputState());
        // _levelLoader.LoadLevel(0);
    }
    
    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var inputSystems = new ParallelSystem<GameState>(_parallelRunner,
            new CursorInputSystem(_defaultEcsWorld, _camera)
            // new InputMappingSystem(_defaultEcsWorld)
        );
        
        var levelLoadSystems = new SequentialSystem<GameState>(
            new LevelLoadRequestSystem(_defaultEcsWorld, _content),
            new LDtkTileParserSystem(_defaultEcsWorld, _content),
            new LDtkEntityParserSystem(_defaultEcsWorld),
            new EntitySpawnSystem(_world, _defaultEcsWorld, _content, _renderTargets));
        
        // Systems that modify component state (can often be parallel)
        var logicSystems = new ParallelSystem<GameState>(_parallelRunner,
            new CursorPositionSystem(_defaultEcsWorld),
            // new MovementSystem(_defaultEcsWorld, _parallelRunner),
            new VelocitySystem(_defaultEcsWorld, _parallelRunner),
            new CollisionDetectionSystem<CollisionMessage>(_defaultEcsWorld, _parallelRunner, CollisionMessage.Create),
            new PhysicalCollisionResolutionSystem(_defaultEcsWorld),
            new PositionSystem(_defaultEcsWorld, _parallelRunner),
            new TextUpdateSystem(_defaultEcsWorld), // Logic only
            new DialogueUpdateSystem(_defaultEcsWorld),
            new CursorDrawPrepSystem(_defaultEcsWorld)
            // ... other game logic systems
        );
        
        var cameraFollowSystem = new CameraFollowSystem(_defaultEcsWorld, _camera);

        var debugSystems = new ParallelSystem<GameState>(_parallelRunner,
            new ColliderDebugSystem(_defaultEcsWorld, _graphicsDevice)
        );
        
        return new SequentialSystem<GameState>(
            // new DebugSystem(_world, _game, _spriteBatch), // If needed
            inputSystems,
            levelLoadSystems,
            logicSystems,
            cameraFollowSystem,
            debugSystems
        );
    }
    
    private SequentialSystem<GameState> CreateDrawSystem()
    {
        // Systems that prepare DrawComponent based on state (can often be parallel)
        var prepDrawSystems = new SequentialSystem<GameState>( // Or parallel if clearing is handled carefully
            // Optional: A system to clear all DrawComponents first?
            // new ClearDrawComponentSystem(_world),
            new CullingSystem(_defaultEcsWorld, _camera),
            new DialogueUIRenderPrepSystem(_defaultEcsWorld),
            new SpritePrepSystem(_defaultEcsWorld, _graphicsDevice),
            new TextPrepSystem(_defaultEcsWorld)
            // ... other systems preparing DrawElements (UI, particles, etc.)
        );

        // The single system that handles all rendering (strictly sequential)
        var renderSystem = new MasterRenderSystem(
            _spriteBatch,
            _graphicsDevice,
            _camera,
            _renderTargets, // Pass the dictionary/collection of RTs
            _defaultEcsWorld
        );
    
        // Final system to draw RenderTargets to backbuffer (if needed)
        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets);
        
        return new SequentialSystem<GameState>(
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem // Draw RTs to screen
            // new DrawDebugSystem(_world, _spriteBatch, _renderer) // If needed
        );
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
}