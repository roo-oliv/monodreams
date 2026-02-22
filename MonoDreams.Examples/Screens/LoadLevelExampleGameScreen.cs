using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
#if DEBUG
using MonoDreams.Examples.Inspector;
#endif
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Input;
using MonoDreams.Input;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Message;
using MonoDreams.Message.Level;
using MonoDreams.Message;
using MonoDreams.Examples.Collision;
using MonoDreams.Examples.System;
using MonoDreams.System;
using MonoDreams.System.Camera;
using MonoDreams.System.EntitySpawn;
using MonoDreams.Examples.EntityFactory;
using MonoDreams.Extension;
using MonoDreams.System.Physics;
using MonoDreams.System.Collision;
using MonoDreams.System.Cursor;
using MonoDreams.System.Debug;
using MonoDreams.Examples.Settings;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.System.Draw;
using MonoDreams.System.Input;
using MonoDreams.System.Level;
using MonoDreams.Level;
using MonoDreams.Draw;
using MonoDreams.Examples.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;
using Camera = MonoDreams.Component.Camera;
using DynamicText = MonoDreams.Component.Draw.DynamicText;

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
#if DEBUG
    private InputMappingSystem _inputMappingSystem;
#endif
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly DrawLayerMap _layers;

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
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) }
        };
        
        camera.Position = new Vector2(0, 0);

        _layers = DrawLayerMap.FromEnum<GameDrawLayer>()
            .WithYSort(GameDrawLayer.Characters);
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

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
            // Add more cursor types as needed
        };

        // Create cursor entity
        Objects.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        // Check if a level was requested from the level selection screen
        var requestedLevel = screenController.Game.Services.GetService(typeof(RequestedLevelComponent)) as RequestedLevelComponent;
        if (requestedLevel != null)
        {
            // Load the requested level
            _world.Publish(new LoadLevelRequest(requestedLevel.LevelIdentifier));

            // Remove the service so it doesn't interfere with future screen loads
            screenController.Game.Services.RemoveService(typeof(RequestedLevelComponent));
        }

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
            inputSystems = new SequentialSystem<GameState>(
                new CursorInputSystem(_world), replaySystem, inputMappingSystem);
        }
        else
        {
            inputSystems = new ParallelSystem<GameState>(_parallelRunner,
                new CursorInputSystem(_world), inputMappingSystem);
        }
        
        var promptFont = _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");

        var blenderParser = new BlenderLevelParserSystem(_world, _content, _camera);
        blenderParser.SetDrawLayerMap(_layers);
        blenderParser.RegisterCollectionHandler("Player", (entity, _) => entity.Set(new PlayerState()));
        blenderParser.RegisterCollectionHandler("NPC", (entity, blenderObj) =>
        {
            var npcName = blenderObj.Name;
            entity.Set(new EntityInfo("NPC", npcName));

            var npcTransform = entity.Get<Transform>();
            var npcSprite = entity.Get<SpriteInfo>();
            var npcDimensions = new Vector2(
                blenderObj.Dimensions?.X ?? npcSprite.Size.X,
                blenderObj.Dimensions?.Y ?? npcSprite.Size.Y);

            // Create interaction zone entity (wider than the NPC for approach detection)
            var zoneEntity = _world.CreateEntity();
            zoneEntity.Set(new EntityInfo("NPCZone"));
            zoneEntity.Set(new Transform());
            zoneEntity.SetParent(entity);

            var zoneWidth = (int)(npcDimensions.X * 2.5f);
            var zoneHeight = (int)(npcDimensions.Y * 1.5f);
            var zoneBounds = new Rectangle(-zoneWidth / 2, -zoneHeight / 2, zoneWidth, zoneHeight);
            zoneEntity.Set(new BoxCollider(zoneBounds, passive: true));
            zoneEntity.Set(new DialogueZoneComponent(npcName, oneTimeOnly: false, autoStart: false, npcName: npcName));

            // Create floating icon entity (above the NPC sprite)
            var iconEntity = _world.CreateEntity();
            iconEntity.Set(new EntityInfo("InteractionIcon", $"{npcName}Icon"));

            // Compute icon position above NPC visual top
            var originOffsetY = blenderObj.OriginOffset?.Y ?? 0.5f;
            var visualTop = -npcDimensions.Y * (1 - originOffsetY);
            var iconOffset = new Vector2(0, visualTop - 6f);

            iconEntity.Set(new Transform(iconOffset));
            iconEntity.SetParent(entity);
            iconEntity.Set(new DynamicText
            {
                Target = RenderTargetID.Main,
                Font = promptFont,
                TextContent = "E",
                Color = Color.White,
                Scale = 0.4f,
                LayerDepth = _layers.GetDepth(GameDrawLayer.Characters, -0.01f),
                IsRevealed = true,
                VisibleCharacterCount = 1,
                RevealingSpeed = 0,
                RevealStartTime = 0
            });
            iconEntity.Set(new DrawComponent
            {
                Type = DrawElementType.Text,
                Target = RenderTargetID.Main
            });
            // No Visible initially — managed by NPCInteractionSystem

            zoneEntity.Set(new NPCInteractionIcon { IconEntity = iconEntity });
        });

        var entitySpawnSystem = new EntitySpawnSystem(_world, _content, _renderTargets);
        entitySpawnSystem.RegisterEntityFactory("Tile", new TileEntityFactory(_layers));
        entitySpawnSystem.RegisterEntityFactory("Wall", new WallEntityFactory(_content, _layers));
        entitySpawnSystem.RegisterEntityFactory("Player", new PlayerEntityFactory(_content, _layers));
        entitySpawnSystem.RegisterEntityFactory("Enemy", new NPCEntityFactory(_content, _layers));

        var levelLoadSystems = new SequentialSystem<GameState>(
            new LevelLoadRequestSystem(_world, _content),
            blenderParser,
            new LDtkTileParserSystem(_world, _content),
            new LDtkEntityParserSystem(_world),
            entitySpawnSystem);
        
        // Collision pipeline must run sequentially (movement → velocity → detect → resolve → commit)
        // Individual systems keep their internal _parallelRunner for entity-level parallelism
        var logicSystems = new SequentialSystem<GameState>(
            new MovementSystem(_world, _parallelRunner),
            new OrbSystem(_world),
            new TransformVelocitySystem(_world, _parallelRunner),
            new TransformCollisionDetectionSystem<CollisionMessage>(_world, _parallelRunner, GameCollisionHelper.Create),
            new TransformPhysicalCollisionResolutionSystem(_world),
            new TransformCommitSystem(_world, _parallelRunner),
            new TextUpdateSystem(_world), // Logic only
            new NPCInteractionSystem(_world),
            new DialogueSystem(_world, _content, _graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight, _layers,
                new[] { "Dialogues/hello_world", "Dialogues/boldo" })
            // ... other game logic systems
        );
        
        // Hierarchy system must run AFTER logic systems modify transforms
        // but BEFORE any systems read world transforms (camera, rendering, etc.)
        var hierarchySystem = new HierarchySystem(_world);

        var cameraFollowSystem = new CameraFollowSystem(_world, _camera);

        // Cursor position must update AFTER camera has moved to avoid 1-frame lag
        var cursorLateUpdateSystem = new CursorPositionSystem(_world, _camera, _viewportManager);

        var debugSystems = new ParallelSystem<GameState>(_parallelRunner,
            new ColliderDebugSystem(_world, _graphicsDevice)
        );

        return new SequentialSystem<GameState>(
            // new DebugSystem(_world, _game, _spriteBatch), // If needed
            inputSystems,
            levelLoadSystems,
            logicSystems,
            hierarchySystem, // Entity hierarchy + transform dirty flag propagation
            cameraFollowSystem,
            cursorLateUpdateSystem,          // Cursor position updates after camera
            new CursorDrawPrepSystem(_world) // Draw prep after position is finalized
            // debugSystems
        );
    }
    
    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = SettingsManager.Instance.Settings.PixelPerfectRendering;

        // Systems that prepare DrawComponent based on state (can often be parallel)
        var prepDrawSystems = new SequentialSystem<GameState>( // Or parallel if clearing is handled carefully
            // Optional: A system to clear all DrawComponents first?
            // new ClearDrawComponentSystem(_world),
            new CullingSystem(_world, _camera),
            new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering),
            new YSortSystem(_world, _camera, _layers),
            new TextPrepSystem(_world, pixelPerfectRendering),
            new MeshPrepSystem(_world)
            // new SpriteDebugSystem(_world)  // Debug visualization for sprite bounds and origins
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
            finalDrawToScreenSystem, // Draw RTs to screen
            screenshotSystem
        );
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}