using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Cursor;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Examples.System.UI;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;
using CursorController = MonoDreams.Component.CursorController;
using Camera = MonoDreams.Component.Camera;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;
using RenderTargetID = MonoDreams.Examples.Component.Draw.RenderTargetID;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// Screen for selecting which level to load.
/// </summary>
public class LevelSelectionScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;

    public LevelSelectionScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
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

        camera.Position = Vector2.Zero;

        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    private ScreenController? _screenController;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;

        // Subscribe to screen transition requests
        _world.Subscribe<ScreenTransitionRequest>(OnScreenTransitionRequest);

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
        };

        // Create cursor entity
        Objects.Cursor.Create(_world, cursorTextures, RenderTargetID.Main);

        // Create level selection UI
        CreateLevelSelectionUI();
    }

    [Subscribe]
    private void OnScreenTransitionRequest(in ScreenTransitionRequest request)
    {
        // Store the requested level in the world so the next screen can access it
        if (request.LevelIdentifier != null)
        {
            _screenController?.Game.Services.AddService(new RequestedLevelComponent(request.LevelIdentifier));
        }

        // Transition to the requested screen
        _screenController?.LoadScreen(request.ScreenName);
    }

    private void CreateLevelSelectionUI()
    {
        var font = _content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        // Lofi color palette
        var darkBrown = new Color(60, 50, 45);        // Main text color
        var terracotta = new Color(200, 120, 80);     // Hover/accent color
        var mutedBrown = new Color(150, 140, 130);    // Disabled color

        // Create title
        CreateTitle("Select Level", font, new Vector2(0, -150), darkBrown);

        // Create level buttons with chained relative transforms
        // Level 1: at (-250, 0) with no parent
        var level1Transform = CreateLevelButton("Level 1", 0, font, new Vector2(-250, 0), null, isClickable: true, darkBrown, terracotta, mutedBrown);
        // Level 2: at (250, 0) relative to Level 1 (world position: -250 + 250 = 0)
        var level2Transform = CreateLevelButton("Level 2", 1, font, new Vector2(250, 0), level1Transform, isClickable: false, darkBrown, terracotta, mutedBrown);
        // Level 3: at (250, 0) relative to Level 2 (world position: 0 + 250 = 250)
        var level3Transform = CreateLevelButton("Level 3", 2, font, new Vector2(250, 0), level2Transform, isClickable: false, darkBrown, terracotta, mutedBrown);
    }

    private Entity CreateTitle(string text, BitmapFont font, Vector2 position, Color color)
    {
        var entity = _world.CreateEntity();

        entity.Set(new Transform(position));
        entity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.9f,
            TextContent = text,
            Font = font,
            Color = color,
            Scale = 0.3f,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        entity.Set(new LevelSelectionEntity());

        return entity;
    }

    private Transform CreateLevelButton(string levelName, int levelIndex, BitmapFont font, Vector2 position, Transform? parentTransform, bool isClickable,
        Color defaultColor, Color hoverColor, Color disabledColor)
    {
        // Create shared transform for button and outline
        var buttonTransform = new Transform(position);
        buttonTransform.Parent = parentTransform;

        // Create button entity (text + interaction)
        var buttonTextEntity = _world.CreateEntity();
        buttonTextEntity.Set(buttonTransform);
        buttonTextEntity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.95f,
            TextContent = levelName,
            Font = font,
            Color = isClickable ? defaultColor : disabledColor,
            Scale = 0.15f,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        buttonTextEntity.Set(new LevelSelectionEntity());

        // Create separate outline entity that shares the same transform
        var outlineEntity = _world.CreateEntity();
        outlineEntity.Set(buttonTransform); // Share the same transform reference
        outlineEntity.Set(new SimpleButton
        {
            Size = new Vector2(200, 60),
            LineThickness = 2f,
            Color = defaultColor,
            TextEntity = buttonTextEntity,
            Target = RenderTargetID.Main
        });
        outlineEntity.Set(new LevelSelector
        {
            LevelIndex = levelIndex,
            LevelName = "Level_0", // The identifier of the level in World.ldtk
            IsClickable = isClickable,
            IsHovered = false,
            DefaultColor = defaultColor,
            HoveredColor = hoverColor,
            DisabledColor = disabledColor
        });
        outlineEntity.Set(new LevelSelectionEntity());

        return buttonTransform;
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var inputSystems = new ParallelSystem<GameState>(_parallelRunner,
            new CursorInputSystem(_world, _camera),
            new CursorPositionSystem(_world)
        );

        var uiSystems = new SequentialSystem<GameState>(
            new ButtonInteractionSystem(_world),
            new ButtonMeshPrepSystem(_world),
            new CursorDrawPrepSystem(_world)
        );

        // Transform hierarchy system must run AFTER any systems that modify transforms
        // but BEFORE any systems read world transforms (rendering, etc.)
        var transformHierarchySystem = new TransformHierarchySystem(_world);

        return new SequentialSystem<GameState>(
            inputSystems,
            uiSystems,
            transformHierarchySystem // Propagate transform hierarchy dirty flags
        );
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var prepDrawSystems = new SequentialSystem<GameState>(
            new TextPrepSystem(_world)
        );

        var renderSystem = new MasterRenderSystem(
            _spriteBatch,
            _graphicsDevice,
            _camera,
            _renderTargets,
            _world
        );

        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets);

        return new SequentialSystem<GameState>(
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem
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
}
