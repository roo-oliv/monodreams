using DefaultEcs.System;
using DefaultEcs.Threading;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Camera;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Camera;
using MonoDreams.Examples.System.Cursor;
using MonoDreams.Examples.System.Debug;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using DefaultEcsWorld = DefaultEcs.World;
using Camera = MonoDreams.Component.Camera;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;

namespace MonoDreams.Examples.Screens;

public class TestGameScreen : IGameScreen
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
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;

    public TestGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
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

        camera.Position = new Vector2(640, 360); // Center of 1280x720

        _world = World.Create();
        _defaultEcsWorld = new DefaultEcsWorld();

        SystemPhase.Initialize(_world);

        RegisterSystems();
        _updatePipeline = CreateUpdatePipeline();
        _drawPipeline = CreateDrawPipeline();

        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    private void RegisterSystems()
    {
        // Input Phase Systems
        InputMappingSystem.Register(_world, _defaultEcsWorld, _gameState);
        CursorInputSystem.Register(_world, _camera);

        // Logic Phase Systems
        CursorPositionSystem.Register(_world);
        MovementSystem.Register(_world, _gameState);
        PositionSystem.Register(_world);

        // Debug Systems
        ColliderDebugSystem.Register(_world, _graphicsDevice);

        // Camera Phase Systems
        CameraFollowSystem.Register(_world, _camera, _gameState);

        // Draw Phase Systems
        CullingSystem.Register(_world, _camera);
        CursorDrawPrepSystem.Register(_world);
        SpritePrepSystem.Register(_world, _graphicsDevice);
        TextPrepSystem.Register(_world);
        MasterRenderSystem.Register(_world, _spriteBatch, _graphicsDevice, _camera, _renderTargets);
        FinalDrawSystem.Register(_world, _spriteBatch, _graphicsDevice, _viewportManager, _renderTargets);
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
        // Load cursor textures
        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
        };

        // Create cursor entity
        Objects.Cursor.Create(_world, cursorTextures, RenderTargetID.Main);
        _world.Set(new InputState());

        // Load square texture for player and walls
        var squareTexture = content.Load<Texture2D>("square");

        // Create player at center (subtle blue-gray color)
        CreatePlayer(_world, squareTexture, new Vector2(640, 360));

        // Create boundary walls (1280x720 screen) - subtle dark gray
        CreateWall(_world, squareTexture, new Vector2(0, 0), new Vector2(1280, 32), new Color(60, 60, 60));      // Top
        CreateWall(_world, squareTexture, new Vector2(0, 688), new Vector2(1280, 32), new Color(60, 60, 60));    // Bottom
        CreateWall(_world, squareTexture, new Vector2(0, 0), new Vector2(32, 720), new Color(60, 60, 60));       // Left
        CreateWall(_world, squareTexture, new Vector2(1248, 0), new Vector2(32, 720), new Color(60, 60, 60));    // Right

        // Create some obstacles in the middle - subtle colors
        CreateWall(_world, squareTexture, new Vector2(400, 300), new Vector2(100, 100), new Color(80, 40, 40));  // Subtle dark red
        CreateWall(_world, squareTexture, new Vector2(800, 400), new Vector2(150, 80), new Color(40, 40, 80));   // Subtle dark blue
    }

    private void CreatePlayer(World world, Texture2D texture, Vector2 position)
    {
        var size = new Vector2(32, 32);
        var positionComponent = new Position(position);
        var velocityComponent = new Velocity();
        var colliderComponent = new BoxCollider(
            bounds: new Rectangle(Point.Zero, size.ToPoint()),
            passive: false,
            enabled: true
        );

        // Create in Flecs (for rendering, camera, input)
        world.Entity("Player")
            .Set(positionComponent)
            .Set(velocityComponent)
            .Set(new InputControlled())
            .Set(colliderComponent)
            .Set(new SpriteInfo
            {
                SpriteSheet = texture,
                Source = new Rectangle(0, 0, texture.Width, texture.Height),
                Size = size,
                Color = new Color(100, 120, 140), // Subtle blue-gray
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f,
                Offset = Vector2.Zero
            })
            .Set(new DrawComponent())
            .Set(new CameraFollowTarget
            {
                IsActive = true,
                MaxDistanceX = 200f,
                MaxDistanceY = 150f,
                DampingX = 5f,
                DampingY = 5f
            });

        // ALSO create in DefaultECS (for collision detection)
        var defaultEcsEntity = _defaultEcsWorld.CreateEntity();
        defaultEcsEntity.Set(positionComponent);
        defaultEcsEntity.Set(velocityComponent);
        defaultEcsEntity.Set(colliderComponent);
        defaultEcsEntity.Set(new EntityInfo(EntityType.Player));
    }

    private void CreateWall(World world, Texture2D texture, Vector2 position, Vector2 size, Color color)
    {
        var positionComponent = new Position(position);
        var colliderComponent = new BoxCollider(
            bounds: new Rectangle(Point.Zero, size.ToPoint()),
            enabled: true,
            passive: true
        );

        // Create in Flecs (for rendering)
        world.Entity()
            .Set(positionComponent)
            .Set(colliderComponent)
            .Set(new SpriteInfo
            {
                SpriteSheet = texture,
                Source = new Rectangle(0, 0, texture.Width, texture.Height),
                Size = size,
                Color = color,
                Target = RenderTargetID.Main,
                LayerDepth = 0.3f,
                Offset = Vector2.Zero
            })
            .Set(new DrawComponent());

        // ALSO create in DefaultECS (for collision detection)
        var defaultEcsEntity = _defaultEcsWorld.CreateEntity();
        defaultEcsEntity.Set(positionComponent);
        defaultEcsEntity.Set(colliderComponent);
        defaultEcsEntity.Set(new EntityInfo(EntityType.Tile));
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        // Only DefaultECS systems that haven't been migrated
        var logicSystems = new ParallelSystem<GameState>(_parallelRunner,
            new VelocitySystem(_defaultEcsWorld, _parallelRunner),
            new CollisionDetectionSystem<CollisionMessage>(_defaultEcsWorld, _parallelRunner, CollisionMessage.Create),
            new PhysicalCollisionResolutionSystem(_defaultEcsWorld)
        );

        return new SequentialSystem<GameState>(logicSystems);
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        // Draw is handled by Flecs pipeline
        return new SequentialSystem<GameState>();
    }

    private Pipeline CreateUpdatePipeline()
    {
        return _world.Pipeline()
            .With(Ecs.System)
            .Without(SystemPhase.DrawPhase)
            .Build();
    }

    private Pipeline CreateDrawPipeline()
    {
        return _world.Pipeline()
            .With(Ecs.System)
            .With(SystemPhase.DrawPhase)
            .Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        foreach (var rt in _renderTargets.Values)
        {
            rt?.Dispose();
        }
        ColliderDebugSystem.Cleanup();
        GC.SuppressFinalize(this);
    }
}
