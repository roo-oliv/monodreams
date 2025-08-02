using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Toolkit.HighPerformance.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.Draw;
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
using MonoGame.SplineFlower;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.Screens;

public class GameJamScreen : IGameScreen
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
    
    public GameJamScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        // viewportManager.SetVirtualResolution(ViewportManager.PresetResolutions[0].width, ViewportManager.PresetResolutions[0].height);
        _viewportManager = viewportManager;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight) }
        };
        
        Setup.Initialize(_graphicsDevice, 10000F);
        Setup.BaseLineColor = Color.DarkRed;
        Setup.CurveLineColor = Color.Black;// new Color(new Vector3(21, 20, 28));
        Setup.ShowPoints = true;
        Setup.ShowCenterSpline = false;
        Setup.ShowTangents = true;
        Setup.ShowDirectionVectors = false;
        Setup.ShowCurves = true;
        Setup.ShowBaseLine = true;
        Setup.CurveLineThickness = 1f;
        Setup.TangentColor = Color.Black;
        
        camera.Position = new Vector2(0, 0);
        
        _world = new World();
        // _levelLoader = new LevelLoader(_world, graphicsDevice, _content, _spriteBatch, _renderTargets);
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public void Load(ScreenController screenController, ContentManager content)
    {
        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            // [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/slick_arrow-delta"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
            // Add more cursor types as needed
        };

        // Create cursor entity
        Objects.Cursor.Create(_world, cursorTextures, RenderTargetID.Main);

        var trackTexture = new Texture2D(_graphicsDevice, 14, 1);
        var asphalt = new Color(new Vector3(21, 20, 28));
        trackTexture.SetData([asphalt, asphalt, asphalt, asphalt, asphalt, Color.Yellow, Color.Yellow, Color.Yellow, Color.Yellow, asphalt, asphalt, asphalt, asphalt, asphalt]);
        Objects.Track.Create(_world, trackTexture);
        Objects.Car.Create(_world, content.Load<Texture2D>("Characters/SportsRacingCar_0"));
        // _levelLoader.LoadLevel(0);
    }
    
    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var inputSystems = new ParallelSystem<GameState>(_parallelRunner,
            new CursorInputSystem(_world, _camera),
            new InputMappingSystem(_world)
        );
        
        var levelLoadSystems = new SequentialSystem<GameState>(
            // new LevelLoadRequestSystem(_world, _content),
            // new LDtkTileParserSystem(_world, _content),
            // new LDtkEntityParserSystem(_world),
            new EntitySpawnSystem(_world, _content, _renderTargets));
        
        // Systems that modify component state (can often be parallel)
        var logicSystems = new ParallelSystem<GameState>(_parallelRunner,
            new CameraInputSystem(_world, _camera),
            new CursorPositionSystem(_world),
            new MovementSystem(_world, _parallelRunner),
            new VelocitySystem(_world, _parallelRunner),
            new CollisionDetectionSystem<CollisionMessage>(_world, _parallelRunner, CollisionMessage.Create),
            new PhysicalCollisionResolutionSystem(_world),
            new TransformSystem(_world, _parallelRunner),
            new TextUpdateSystem(_world), // Logic only
            new DialogueUpdateSystem(_world),
            new CursorDrawPrepSystem(_world),
            new SplineTransformControlSystem(_world),
            new RaceCarSystem(_world)
            // ... other game logic systems
        );
        
        var cameraFollowSystem = new CameraFollowSystem(_world, _camera);

        var debugSystems = new ParallelSystem<GameState>(_parallelRunner,
            new ColliderDebugSystem(_world, _graphicsDevice)
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
            // new CullingSystem(_world, _camera),
            new DialogueUIRenderPrepSystem(_world),
            new SpritePrepSystem(_world, _graphicsDevice),
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
        
        return new SequentialSystem<GameState>(
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem // Draw RTs to screen
            // new DrawDebugSystem(_world, _spriteBatch, _renderer) // If needed
        );
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}