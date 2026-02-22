using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Layout;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.Settings;
using MonoDreams.Examples.System;
using MonoDreams.Extension;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.Examples.System.Layout;
using MonoDreams.Examples.System.UI;
using MonoDreams.Renderer;
using MonoDreams.Draw;
using MonoDreams.Examples.Draw;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;
using Camera = MonoDreams.Component.Camera;
using DynamicText = MonoDreams.Component.Draw.DynamicText;
using RenderTargetID = MonoDreams.Component.Draw.RenderTargetID;
using Visible = MonoDreams.Component.Draw.Visible;

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
    private readonly BitmapFont _font;
    private readonly DrawLayerMap _layers;

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
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) }
        };

        // Load font early so it's available for systems
        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        camera.Position = Vector2.Zero;

        _layers = DrawLayerMap.FromEnum<DrawLayer>();
        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    private ScreenController? _screenController;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

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
        Objects.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

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
        // Lofi color palette
        var darkBrown = new Color(60, 50, 45);        // Main text color
        var terracotta = new Color(200, 120, 80);     // Hover/accent color
        var mutedBrown = new Color(150, 140, 130);    // Disabled color

        // Create button style
        var buttonStyle = ButtonStyle.WithColors(darkBrown, terracotta, mutedBrown);

        // Create entities first
        var titleEntity = CreateTextEntity("Select Level", _font, darkBrown, scale: 0.3f, _layers.GetDepth(DrawLayer.Title));
        var button1 = CreateButtonEntity("Level 1", _font, 0, "Level_0", true, buttonStyle);
        var button2 = CreateButtonEntity("Level 2", _font, 1, "Blender_Level", true, buttonStyle);
        var button3 = CreateButtonEntity("Level 3", _font, 2, null, true, buttonStyle, ScreenName.InfiniteRunner);

        // Create UI using auto layout with slots
        var layout = new AutoLayoutBuilder(_world, _viewportManager);

        layout.CreateRoot(ScreenAnchor.Center)
            .Name("Root")
            .Direction(LayoutDirection.Vertical)
            .Gap(40)
            .AlignMain(MainAxisAlignment.Center)
            .AlignCross(CrossAxisAlignment.Center)
            .AddSlot(slot => slot
                .Attach(titleEntity)
                .MeasureWith(MeasureText))
            .AddContainer(row => row
                .Name("ButtonColumn")
                .Direction(LayoutDirection.Vertical)
                .Gap(50)
                .AlignCross(CrossAxisAlignment.Center)
                .AddSlot(slot => slot.Attach(button1.container).MeasureWith(_ => button1.size))
                .AddSlot(slot => slot.Attach(button2.container).MeasureWith(_ => button2.size))
                .AddSlot(slot => slot.Attach(button3.container).MeasureWith(_ => button3.size))
            )
            .Build();
    }

    private Entity CreateTextEntity(string text, BitmapFont font, Color color, float scale, float layerDepth)
    {
        var entity = _world.CreateEntity();
        entity.Set(new Transform(Vector2.Zero));
        entity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = layerDepth,
            TextContent = text,
            Font = font,
            Color = color,
            Scale = scale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        entity.Set<Visible>();
        return entity;
    }

    private (Entity container, Vector2 size) CreateButtonEntity(
        string text,
        BitmapFont font,
        int levelIndex,
        string levelName,
        bool isClickable,
        ButtonStyle style,
        string targetScreen = null)
    {
        // Measure text to determine button size
        var textSize = font.MeasureString(text) * style.TextScale;
        var buttonSize = new Vector2(
            textSize.Width + style.Padding * 2,
            textSize.Height + style.Padding * 2);

        // Create button container entity
        var buttonContainerEntity = _world.CreateEntity();
        var buttonTransform = new Transform(Vector2.Zero);
        buttonContainerEntity.Set(buttonTransform);

        // Create button text entity with its own transform, offset by padding to center text
        var buttonTextEntity = _world.CreateEntity();
        buttonTextEntity.Set(new Transform(new Vector2(style.Padding, style.Padding)));
        buttonTextEntity.SetParent(buttonContainerEntity);
        buttonTextEntity.Set(new DynamicText
        {
            Target = RenderTargetID.Main,
            LayerDepth = _layers.GetDepth(DrawLayer.ButtonText),
            TextContent = text,
            Font = font,
            Color = isClickable ? style.DefaultColor : style.DisabledColor,
            Scale = style.TextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        buttonTextEntity.Set<Visible>();

        // Create outline entity (shares transform with button)
        var outlineEntity = _world.CreateEntity();
        outlineEntity.Set(buttonTransform); // Share transform
        outlineEntity.Set(new SimpleButton
        {
            Size = buttonSize,
            LineThickness = style.BorderThickness,
            Color = style.BorderColor,
            TextEntity = buttonTextEntity,
            Target = RenderTargetID.Main
        });
        outlineEntity.Set(new LevelSelector
        {
            LevelIndex = levelIndex,
            LevelName = levelName,
            TargetScreen = targetScreen,
            IsClickable = isClickable,
            IsHovered = false,
            DefaultColor = style.DefaultColor,
            HoveredColor = style.HoveredColor,
            DisabledColor = style.DisabledColor
        });
        outlineEntity.Set<Visible>();

        return (buttonContainerEntity, buttonSize);
    }

    private static Vector2 MeasureText(Entity entity)
    {
        if (!entity.Has<DynamicText>()) return Vector2.Zero;

        ref var text = ref entity.Get<DynamicText>();
        var measuredSize = text.Font.MeasureString(text.TextContent);
        return new Vector2(measuredSize.Width * text.Scale, measuredSize.Height * text.Scale);
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var inputSystems = new CursorInputSystem(_world);

        // Layout systems must run before UI systems to position elements
        var layoutSystems = new SequentialSystem<GameState>(
            new IntrinsicSizingSystem(_world),     // Measure content sizes via callbacks
            new AutoLayoutSystem(_world, _viewportManager),  // Calculate and apply layout
            new LayoutDebugSystem(_world, _font, _camera)   // Debug visualization (toggle with LayoutDebugSystem.Enabled)
        );

        var uiSystems = new SequentialSystem<GameState>(
            new ButtonInteractionSystem(_world),
            new ButtonMeshPrepSystem(_world)
        );

        // Hierarchy system must run AFTER any systems that modify transforms
        // but BEFORE any systems read world transforms (rendering, etc.)
        var hierarchySystem = new HierarchySystem(_world);

        // Cursor position must update after layout/UI to use current camera state
        var cursorLateUpdateSystem = new CursorPositionSystem(_world, _camera, _viewportManager);

        return new SequentialSystem<GameState>(
            inputSystems,
            layoutSystems,  // Layout before UI interaction
            uiSystems,
            hierarchySystem,                  // Entity hierarchy + transform dirty flag propagation
            cursorLateUpdateSystem,           // Cursor position updates after camera
            new CursorDrawPrepSystem(_world)  // Draw prep after position is finalized
        );
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = SettingsManager.Instance.Settings.PixelPerfectRendering;

        var prepDrawSystems = new SequentialSystem<GameState>(
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

    public enum DrawLayer
    {
        Cursor,      // 1.0 - front
        ButtonText,  // middle
        Title,       // 0.0 - back
    }
}
