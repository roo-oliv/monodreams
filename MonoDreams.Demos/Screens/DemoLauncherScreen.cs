using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Demos.UI;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demos.Screens;

/// Menu screen listing every available module demo. Click a button to switch
/// into the demo screen.
public class DemoLauncherScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;

    private ScreenController? _screenController;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public DemoLauncherScreen(GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, SpriteBatch spriteBatch)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
        };

        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");
        camera.Position = Vector2.Zero;

        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnDemoButtonClicked);

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Cursor/default"),
            [CursorType.Pointer] = content.Load<Texture2D>("Cursor/pointer"),
            [CursorType.Hand] = content.Load<Texture2D>("Cursor/hand"),
        };
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        BuildUI();
    }

    private void OnDemoButtonClicked(in DemoButtonClicked msg)
    {
        if (_screenController == null) return;
        switch (msg.Id)
        {
            case "camera":
                _screenController.LoadScreen(DemoScreens.Camera);
                break;
            case "physics":
                _screenController.LoadScreen(DemoScreens.Physics);
                break;
            case "dialogue":
                _screenController.LoadScreen(DemoScreens.Dialogue);
                break;
            case DemoHeader.ExitId:
                _screenController.Game.Exit();
                break;
        }
    }

    private void BuildUI()
    {
        var style = new ButtonStyle
        {
            DefaultColor = SproutPalette.TextLight,
            HoveredColor = SproutPalette.TextHover,
            DisabledColor = SproutPalette.MutedBrown,
            BorderColor = SproutPalette.TextLight,
            BorderThickness = 2f,
            Padding = 18f,
            TextScale = 0.22f,
        };

        var title = DemoUI.CreateText(_world, "MonoDreams Module Demos", _font,
            SproutPalette.TextLight, scale: 0.40f, layerDepth: 0.5f);
        var subtitle = DemoUI.CreateText(_world, "Pick a module below to open its working demonstration.", _font,
            SproutPalette.TextHover, scale: 0.20f, layerDepth: 0.5f);

        var cameraBtn = DemoUI.CreateButton(_world,
            id: "camera",
            label: "camera",
            _font, style,
            textLayerDepth: 0.6f,
            activeColor: SproutPalette.TextSelected);

        var physicsBtn = DemoUI.CreateButton(_world,
            id: "physics",
            label: "physics",
            _font, style,
            textLayerDepth: 0.6f,
            activeColor: SproutPalette.TextSelected);

        var dialogueBtn = DemoUI.CreateButton(_world,
            id: "dialogue",
            label: "dialogue",
            _font, style,
            textLayerDepth: 0.6f,
            activeColor: SproutPalette.TextSelected);

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.Center)
            .Direction(LayoutDirection.Vertical)
            .Gap(20)
            .AlignCross(CrossAxisAlignment.Center)
            .AddSlot(slot => slot.Attach(title).MeasureWith(DemoUI.MeasureText))
            .AddSlot(slot => slot.Attach(subtitle).MeasureWith(DemoUI.MeasureText))
            .AddSlot(slot => slot.Attach(_world.CreateEntity()).MeasureWith(_ => new Vector2(0, 16)))
            .AddSlot(slot => slot.Attach(cameraBtn.container).MeasureWith(_ => cameraBtn.size))
            .AddSlot(slot => slot.Attach(physicsBtn.container).MeasureWith(_ => physicsBtn.size))
            .AddSlot(slot => slot.Attach(dialogueBtn.container).MeasureWith(_ => dialogueBtn.size))
            .Build();

        // Single exit chrome button (top-right) styled as a Q-key chip so it
        // matches the demo header's exit row.
        var squareButtons = _content.Load<Texture2D>("SproutLands/Buttons/square_26x26");
        var capStyle = new KeyCapStyle
        {
            SpriteSheet = squareButtons,
            DefaultSource = SproutSquareButtons.CreamLight,
            HoverSource = SproutSquareButtons.CreamDark,
            ActiveSource = SproutSquareButtons.TanDark,
            CapPixels = 32,
            CapLabelScale = 0.13f,
            CapLabelColor = SproutPalette.WarmBrown,
        };
        var rowStyle = new KeyRowStyle
        {
            LabelColor = SproutPalette.TextLight,
            HoverColor = SproutPalette.TextHover,
            ActiveColor = SproutPalette.TextSelected,
            LabelScale = 0.18f,
            Gap = 8f,
            BackgroundColor = SproutPalette.DarkBgSecondary,
            HoverBackgroundColor = SproutPalette.DarkBgSecondary,
            ActiveBackgroundColor = SproutPalette.DarkBgSecondary,
            BackgroundPaddingX = 10f,
            BackgroundPaddingY = 6f,
        };
        var exitRow = _world.CreateKeyRow(
            id: DemoHeader.ExitId, keyLabel: "Q", rowLabel: "exit",
            font: _font, cap: capStyle, row: rowStyle,
            layerDepth: 0.95f, target: RenderTargetID.HUD);

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopRight, RenderTargetID.HUD)
            .Direction(LayoutDirection.Horizontal)
            .Padding(4 /* top */, 8 /* right */, 12 /* bottom */, 8 /* left */)
            .AddSlot(slot => slot.Attach(exitRow.Container).MeasureWith(_ => exitRow.Size))
            .Build();
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        return new SequentialSystem<GameState>(
            new CursorInputSystem(_world),
            new IntrinsicSizingSystem(_world),
            new AutoLayoutSystem(_world, _viewportManager),
            new DemoButtonInteractionSystem(_world),
            new DemoIconRecolorSystem(_world),
            new HierarchySystem(_world),
            new CursorPositionSystem(_world, _camera, _viewportManager),
            new CursorDrawPrepSystem(_world));
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        return new SequentialSystem<GameState>(
            new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false),
            new TextPrepSystem(_world, pixelPerfectRendering: false),
            new MeshPrepSystem(_world),
            // ButtonMeshPrepSystem must run AFTER MeshPrepSystem: button outlines are
            // baked in world coords and clear WorldMatrix to identity.
            new ButtonMeshPrepSystem(_world),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.UI, _renderTargets[RenderTargetID.UI]),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]),
            new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, new[]
            {
                RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
                RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
                RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
            }));
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }
}
