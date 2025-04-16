using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using LDtk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Dialogue;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.Message.Level;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Examples.System.InGameDebug;
using MonoDreams.Examples.System.Level;
using MonoDreams.Objects;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Draw;
using MonoDreams.System.Physics;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Screens;

public class LoadLevelExampleGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly LevelLoader _levelLoader;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    
    public LoadLevelExampleGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
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
        
        _world = new World();
        // _levelLoader = new LevelLoader(_world, graphicsDevice, _content, _spriteBatch, _renderTargets);
        System = CreateSystem();
    }

    public ISystem<GameState> System { get; }
    public void Load(ScreenController screenController, ContentManager content)
    {
        // _levelLoader.LoadLevel(0);
        InGameDebug.Create(_world, _spriteBatch, _viewportManager);
    }
    
    private SequentialSystem<GameState> CreateSystem()
    {
        var inputMappingSystem = new InputMappingSystem(_world);
        
        // Systems that modify component state (can often be parallel)
        var logicSystems = new ParallelSystem<GameState>(_parallelRunner,
            
            // new MovementSystem(),
            // new VelocitySystem(),
            // new CollisionDetectionSystem(),
            // new PhysicalCollisionResolutionSystem(),
            // new PositionSystem(),
            new TextUpdateSystem(_world), // Logic only
            new DialogueUpdateSystem(_world)
            // ... other game logic systems
        );

        // Systems that prepare DrawComponent based on state (can often be parallel)
        var prepDrawSystems = new SequentialSystem<GameState>( // Or parallel if clearing is handled carefully
            // Optional: A system to clear all DrawComponents first?
            // new ClearDrawComponentSystem(_world),
            new TilemapRenderPrepSystem(_world, _content),
            new DialogueUIRenderPrepSystem(_world),
            new SpritePrepSystem(_world),
            new TextPrepSystem(_world)
            // ... other systems preparing DrawElements (UI, particles, etc.)
        );

        // The single system that handles all rendering (strictly sequential)
        var renderSystem = new MasterRenderSystem(
            _spriteBatch,
            _graphicsDevice,
            _camera,
            _renderTargets, // Pass the dictionary/collection of RTs
            _world
        );
    
        // Final system to draw RenderTargets to backbuffer (if needed)
        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets);
        
        var levelLoadSystem = new LevelLoadRequestSystem(_world, _content);

        var entityParserSystem = new LDtkEntityParserSystem(_world);
        
        return new SequentialSystem<GameState>(
            // new DebugSystem(_world, _game, _spriteBatch), // If needed
            inputMappingSystem,
            levelLoadSystem,
            entityParserSystem,
            logicSystems,
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem // Draw RTs to screen
            // new DrawDebugSystem(_world, _spriteBatch, _renderer) // If needed
        );
    }

    public void Dispose()
    {
        System.Dispose();
        GC.SuppressFinalize(this);
    }
}