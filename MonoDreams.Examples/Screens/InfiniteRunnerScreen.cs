using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
#if DEBUG
using MonoDreams.Examples.Inspector;
#endif
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Runner;
using MonoDreams.Examples.System;
using MonoDreams.Input;
using MonoDreams.Message;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;
using MonoDreams.System.Input;
using MonoDreams.System.Physics;
using Camera = MonoDreams.Component.Camera;

namespace MonoDreams.Examples.Screens;

public class InfiniteRunnerScreen : IGameScreen
{
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ContentManager _content;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly MonoGame.Extended.BitmapFonts.BitmapFont _font;
    private readonly DrawLayerMap _layers;
#if DEBUG
    private InputMappingSystem _inputMappingSystem;
#endif

    public InfiniteRunnerScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
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
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) }
        };

        _font = content.Load<MonoGame.Extended.BitmapFonts.BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");
        camera.Zoom = RunnerConstants.CameraZoom;
        camera.Position = RunnerConstants.CameraPosition;

        _layers = DrawLayerMap.FromEnum<RunnerDrawLayer>();
        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public void Load(ScreenController screenController, ContentManager content)
    {
#if DEBUG
        // Wire debug inspector input suppression
        var debugInspector = screenController.Game.Services.GetService(typeof(DebugInspector)) as DebugInspector;
        if (debugInspector != null)
        {
            _inputMappingSystem.ShouldSuppressInput = () => debugInspector.WantsKeyboard;
        }
#endif

        Logger.Info("Loading InfiniteRunner screen.");
        CreateTreadmill();
        CreatePlayer();
        CreateSpawnPoint();
        CreateScoreHUD(content);
        Logger.Info("InfiniteRunner screen loaded.");
    }

    private void CreateTreadmill()
    {
        // Invisible collider for physics
        var collider = _world.CreateEntity();
        collider.Set(new EntityInfo("Wall"));
        collider.Set(new Transform(new Vector2(0, RunnerConstants.TreadmillY)));
        collider.Set(new BoxCollider(
            new Rectangle(0, 0, (int)RunnerConstants.TreadmillTotalWidth, (int)RunnerConstants.TreadmillSegmentHeight),
            passive: true));
        collider.Set(new RigidBody(isKinematic: true, gravityActive: false));

        // Cosmetic segments — top row (scrolls left)
        for (int i = 0; i < RunnerConstants.TreadmillSegmentCount; i++)
        {
            CreateTreadmillSegment(i, RunnerConstants.TreadmillY, RunnerConstants.TreadmillColor, isTopRow: true);
        }

        // Cosmetic segments — bottom row (scrolls right)
        var bottomY = RunnerConstants.TreadmillY + RunnerConstants.TreadmillSegmentHeight + RunnerConstants.BottomRowGap;
        for (int i = 0; i < RunnerConstants.TreadmillSegmentCount; i++)
        {
            CreateTreadmillSegment(i, bottomY, RunnerConstants.TreadmillBottomColor, isTopRow: false);
        }
    }

    private void CreateTreadmillSegment(int index, float y, Color color, bool isTopRow)
    {
        var x = index * (RunnerConstants.TreadmillSegmentWidth + RunnerConstants.TreadmillSegmentGap);
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfo("Interface"));
        entity.Set(new Transform(new Vector2(x, y)));

        var mesh = new FilledRectangleMeshGenerator(
            new Rectangle(0, 0, (int)RunnerConstants.TreadmillSegmentWidth, (int)RunnerConstants.TreadmillSegmentHeight),
            color).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = mesh.Vertices,
            Indices = mesh.Indices,
            PrimitiveType = mesh.PrimitiveType,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.Treadmill)
        });
        entity.Set(new Visible());
        entity.Set(new TreadmillSegment { IsTopRow = isTopRow });
    }

    private void CreateSpawnPoint()
    {
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfo("Interface"));
        entity.Set(new Transform(new Vector2(RunnerConstants.SpawnPointX, RunnerConstants.SpawnPointBaseY)));
        entity.Set(new SpawnPoint());

        var circleMesh = new CircleMeshGenerator(
            Vector2.Zero,
            RunnerConstants.SpawnPointRadius,
            RunnerConstants.SpawnPointColor,
            segments: 16).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = circleMesh.Vertices,
            Indices = circleMesh.Indices,
            PrimitiveType = circleMesh.PrimitiveType,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.SpawnPoint)
        });
        entity.Set(new Visible());
    }

    private void CreatePlayer()
    {
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfo("Player"));
        entity.Set(new Transform(RunnerConstants.PlayerStartPosition));
        entity.Set(new BoxCollider(
            new Rectangle(
                RunnerConstants.PlayerColliderOffset.X,
                RunnerConstants.PlayerColliderOffset.Y,
                RunnerConstants.PlayerColliderSize.X,
                RunnerConstants.PlayerColliderSize.Y)));
        entity.Set(new RigidBody());
        entity.Set(new Velocity());
        entity.Set(new RunnerState());

        var circleMesh = new CircleMeshGenerator(
            Vector2.Zero,
            RunnerConstants.PlayerRadius,
            RunnerConstants.PlayerColor,
            segments: 24).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = circleMesh.Vertices,
            Indices = circleMesh.Indices,
            PrimitiveType = circleMesh.PrimitiveType,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.Player)
        });
        entity.Set(new Visible());
    }

    private void CreateScoreHUD(ContentManager content)
    {
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfo("Interface"));
        entity.Set(new Transform(RunnerConstants.ScorePosition));
        entity.Set(new DynamicText
        {
            Target = RenderTargetID.HUD,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.HUD),
            TextContent = "Score: 0",
            Font = _font,
            Color = RunnerConstants.ScoreColor,
            Scale = RunnerConstants.ScoreTextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        entity.Set(new Visible());
        entity.Set(new ScoreDisplay());
    }

    private static CollisionMessage CreateRunnerCollision(
        Entity entity, Entity target, Vector2 contactPoint, Vector2 contactNormal, float contactTime, float penetrationDepth, int layer)
    {
        var entityType = entity.Get<EntityInfo>().Type;
        var targetType = target.Get<EntityInfo>().Type;
        var type = (entityType, targetType) switch
        {
            ("Player", "Collectible") => CollisionType.Collectible,
            ("Player", "Obstacle") => CollisionType.Damage,
            _ => CollisionType.Physics
        };
        return new CollisionMessage(entity, target, contactPoint, contactNormal, contactTime, penetrationDepth, layer, type);
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var debugDir = Environment.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
        var inputMappingSystem = new InputMappingSystem(_world);
#if DEBUG
        _inputMappingSystem = inputMappingSystem;
#endif
        var actionMap = new Dictionary<string, AInputState>
        {
            ["Up"] = InputState.Up, ["Down"] = InputState.Down,
            ["Left"] = InputState.Left, ["Right"] = InputState.Right,
            ["Jump"] = InputState.Jump, ["Grab"] = InputState.Grab,
            ["Orb"] = InputState.Orb, ["Exit"] = InputState.Exit,
            ["Interact"] = InputState.Interact,
        };

        var replaySystem = InputReplaySystem.TryLoad(debugDir, actionMap, _game);

        ISystem<GameState> inputSystems;
        if (replaySystem != null)
        {
            inputMappingSystem.SkipHardwareRead = true;
            inputSystems = new SequentialSystem<GameState>(replaySystem, inputMappingSystem);
        }
        else
        {
            inputSystems = inputMappingSystem;
        }

        var runnerMovement = new MonoDreams.Examples.System.Runner.RunnerMovementSystem(_world);
        var treadmillScroll = new MonoDreams.Examples.System.Runner.TreadmillScrollSystem(_world);
        var runnerSpawner = new MonoDreams.Examples.System.Runner.RunnerSpawnerSystem(_world);
        var runnerCollisionHandler = new MonoDreams.Examples.System.Runner.RunnerCollisionHandlerSystem(_world);
        var gameOver = new MonoDreams.Examples.System.Runner.GameOverSystem(_world, _game, _font);
        var offScreenCleanup = new MonoDreams.Examples.System.Runner.OffScreenCleanupSystem(_world);
        var scoreDisplay = new MonoDreams.Examples.System.Runner.ScoreDisplaySystem(_world);

        var entitySpawnSystem = new MonoDreams.System.EntitySpawn.EntitySpawnSystem(_world, null, _renderTargets);
        entitySpawnSystem.RegisterEntityFactory("Charm", new MonoDreams.Examples.EntityFactory.CharmFactory(_layers));
        entitySpawnSystem.RegisterEntityFactory("Obstacle", new MonoDreams.Examples.EntityFactory.ObstacleFactory(_layers));

        var logicSystems = new SequentialSystem<GameState>(
            entitySpawnSystem,
            runnerMovement,
            new GravitySystem(_world, _parallelRunner, RunnerConstants.WorldGravity, RunnerConstants.MaxFallVelocity),
            treadmillScroll,
            runnerSpawner,
            new TransformVelocitySystem(_world, _parallelRunner),
            new TransformCollisionDetectionSystem<CollisionMessage>(_world, _parallelRunner, CreateRunnerCollision),
            new TransformPhysicalCollisionResolutionSystem(_world),
            runnerCollisionHandler,
            new TransformCommitSystem(_world, _parallelRunner),
            gameOver,
            offScreenCleanup,
            scoreDisplay
        );

        var hierarchySystem = new HierarchySystem(_world);

        return new SequentialSystem<GameState>(
            inputSystems,
            logicSystems,
            hierarchySystem
        );
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = MonoDreams.Examples.Settings.SettingsManager.Instance.Settings.PixelPerfectRendering;

        var prepDrawSystems = new SequentialSystem<GameState>(
            new MeshPrepSystem(_world),
            new TextPrepSystem(_world, pixelPerfectRendering)
        );

        var renderSystem = new MasterRenderSystem(
            _spriteBatch,
            _graphicsDevice,
            _camera,
            _renderTargets,
            _world
        );

        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets);

        var debugDir = Environment.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        var screenshotSystem = new ScreenshotCaptureSystem(_graphicsDevice, captureIntervalSeconds: 2.0f, debugDir)
        {
            IsEnabled = replayPlan?.Screenshots ?? false
        };

        return new SequentialSystem<GameState>(
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem,
            screenshotSystem
        );
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values)
        {
            rt.Dispose();
        }
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    public enum RunnerDrawLayer
    {
        HUD,         // front - score display
        Player,      // player circle
        Collectible, // charms
        Obstacle,    // obstacles
        Treadmill,   // treadmill segments
        SpawnPoint,  // spawn point indicator (back)
    }
}
