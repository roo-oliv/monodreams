using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Screens;
using MonoDreams.Examples.Settings;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;

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

    public Game1()
    {
        // Load settings first
        _settings = SettingsManager.Instance.Settings;

        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;

        // Apply settings
        _graphics.IsFullScreen = _settings.IsFullscreen;
        _graphics.PreferredBackBufferWidth = _settings.WindowWidth;
        _graphics.PreferredBackBufferHeight = _settings.WindowHeight;

        _graphics.SynchronizeWithVerticalRetrace = true;
        _graphics.ApplyChanges();

        // Initialize with virtual resolution from settings
        _viewportManager = new(this, _settings.VirtualWidth, _settings.VirtualHeight);
        _camera = new(_settings.VirtualWidth, _settings.VirtualHeight);

        // Add window resize handling
        Window.ClientSizeChanged += OnWindowResize;
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

        // _screenController.RegisterScreen(ScreenName.Game, () => new DreamGameScreen(this, GraphicsDevice, Content, _camera, _renderer, _runner, _spriteBatch));
        // _screenController.RegisterScreen(ScreenName.Game, () => new DialogueExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.LevelSelection, () => new LevelSelectionScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.Game, () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.MainMenu, () => new MainMenuScreen(GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.OptionsMenu, () => new OptionsMenuScreen(GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));

        _screenController.LoadScreen(ScreenName.LevelSelection);
        
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // GraphicsDevice.Clear(Color.OldLace);
        _screenController.Update(gameTime);
    }
    
    protected override void Draw(GameTime gameTime)
    {
        _screenController.Draw(gameTime);
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
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
        base.Dispose(disposing);
    }
}