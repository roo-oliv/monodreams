using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#if DEBUG
using MonoGame.ImGuiNet;
using MonoDreams.Examples.Inspector;
#endif
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Screens;
using MonoDreams.Examples.Settings;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Input;
using MonoDreams.Screen;
using MonoDreams.State;

namespace MonoDreams.Examples;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private ViewportManager _viewportManager;
    private Camera _camera;
    private DefaultParallelRunner _runner;
    private ScreenController _screenController;
    private GameSettings _settings;
    private readonly bool _headless;
#if DEBUG
    private ImGuiRenderer _imGuiRenderer;
    private DebugInspector _debugInspector;
#endif

    public Game1(string[] args = null)
    {
        _headless = args?.Contains("--headless") ?? false;

        // Load settings first
        _settings = SettingsManager.Instance.Settings;

        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        // GraphicsProfile is platform-conditional: desktop GL supports HiDef; a web
        // (BlazorGL/WebGL) head defines MONODREAMS_WEB and falls back to Reach.
        // The platform is chosen by the head's build, never baked into engine source.
#if MONODREAMS_WEB
        _graphics.GraphicsProfile = GraphicsProfile.Reach;
#else
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif

        if (_headless)
        {
            _graphics.PreferredBackBufferWidth = 1;
            _graphics.PreferredBackBufferHeight = 1;
            _graphics.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = false;
        }
        else
        {
            IsFixedTimeStep = true;
            _graphics.IsFullScreen = _settings.IsFullscreen;
            _graphics.PreferredBackBufferWidth = _settings.WindowWidth;
            _graphics.PreferredBackBufferHeight = _settings.WindowHeight;
            _graphics.SynchronizeWithVerticalRetrace = true;
        }
        _graphics.ApplyChanges();

        // Initialize with virtual resolution from settings
        _viewportManager = new(this, _settings.VirtualWidth, _settings.VirtualHeight);
        _camera = new(_settings.VirtualWidth, _settings.VirtualHeight);

        // Window resize handling is a desktop concern; a web head sizes the canvas
        // from the host page, so the OS-window event is gated out there.
#if !MONODREAMS_WEB
        Window.ClientSizeChanged += OnWindowResize;
#endif
    }
    
    private void OnWindowResize(object sender, EventArgs e)
    {
        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }
    
    private void InitializeRenderer(int realScreenWidth, int realScreenHeight)
    {
        _viewportManager.ScreenWidth = realScreenWidth;
        _viewportManager.ScreenHeight = realScreenHeight;
        _camera.RecalculateTransformationMatrices();
    }

    protected override void Initialize()
    {
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        Logger.Initialize(debugDir);

        if (_headless)
        {
            // Off-screen window positioning is a desktop-only headless trick.
#if !MONODREAMS_WEB
            Window.Position = new Point(-2000, -2000);
#endif
            Logger.Info("Running in headless mode.");
        }

        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        // Apply scaling mode from settings
        _viewportManager.CurrentScalingMode = _settings.ScalingMode switch
        {
            "PixelPerfect" => ViewportManager.ScalingMode.PixelPerfect,
            "Smooth" => ViewportManager.ScalingMode.Smooth,
            _ => ViewportManager.ScalingMode.KeepAspectRatio
        };

        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;

        _spriteBatch = new(GraphicsDevice);
        _runner = new(1);
        _screenController = new(this, _runner, _viewportManager, _camera, _spriteBatch, Content);
        _camera.Zoom = _settings.CameraZoom;
        _camera.Position = Vector2.Zero;

#if DEBUG
        if (!_headless)
        {
            _imGuiRenderer = new ImGuiRenderer(this);
            _imGuiRenderer.RebuildFontAtlas();
            _debugInspector = new DebugInspector();
            Services.AddService(_debugInspector);
        }
#endif

        _screenController.RegisterScreen(ScreenName.LevelSelection, () => new LevelSelectionScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.Game, () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.InfiniteRunner, () => new InfiniteRunnerScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));

        // If a replay plan specifies a start screen or level, skip menus
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        if (replayPlan?.StartScreen != null)
        {
            Logger.Info($"Replay plan detected. Skipping to screen '{replayPlan.StartScreen}'.");
            _screenController.LoadScreen(replayPlan.StartScreen);
        }
        else if (replayPlan?.StartLevel != null)
        {
            Logger.Info($"Replay plan detected. Skipping to level '{replayPlan.StartLevel}'.");
            Services.AddService(new RequestedLevelComponent(replayPlan.StartLevel));
            _screenController.LoadScreen(ScreenName.Game);
        }
        else
        {
            _screenController.LoadScreen(ScreenName.LevelSelection);
        }

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
#if DEBUG
        _debugInspector?.HandleInput();
#endif

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        _screenController.Update(gameTime);
    }
    
    protected override void Draw(GameTime gameTime)
    {
        if (_headless) return;
        _screenController.Draw(gameTime);

#if DEBUG
        _imGuiRenderer?.BeforeLayout(gameTime);
        _debugInspector?.Draw(_screenController.CurrentWorld);
        _imGuiRenderer?.AfterLayout();
#endif
    }

    /// <summary>
    /// Applies new resolution settings at runtime.
    /// </summary>
    public void ApplyResolutionSettings(int width, int height, bool fullscreen)
    {
        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.IsFullScreen = fullscreen;
        _graphics.ApplyChanges();
        InitializeRenderer(width, height);
    }

    protected override void Dispose(bool disposing)
    {
        _screenController.Dispose();
        Logger.Shutdown();
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
#if DEBUG
        (_imGuiRenderer as IDisposable)?.Dispose();
#endif
        base.Dispose(disposing);
    }
}