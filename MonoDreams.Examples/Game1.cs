using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Screens;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace MonoDreams.Examples;

public class Game1 : Game
{
    // Add resolution configuration
    private const int VIRTUAL_WIDTH = 1920;   // Larger virtual resolution for UHD
    private const int VIRTUAL_HEIGHT = 1080;
    
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private ViewportManager _viewportManager;
    private Camera _camera;
    private DefaultParallelRunner _runner;
    private ScreenController _screenController;

    
    
    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;

        _graphics.IsFullScreen = true;
        // Use standard resolution initially, we'll adjust in Initialize()
        _graphics.PreferredBackBufferWidth = 1920;
        _graphics.PreferredBackBufferHeight = 1080;
        
        _graphics.SynchronizeWithVerticalRetrace = true;
        _graphics.ApplyChanges();
        
        // Initialize with larger virtual resolution
        _viewportManager = new ViewportManager(this, VIRTUAL_WIDTH, VIRTUAL_HEIGHT);
        _camera = new Camera(VIRTUAL_WIDTH, VIRTUAL_HEIGHT);
        
        // Add window resize handling
        Window.ClientSizeChanged += OnWindowResize;
    }
    
    private void OnWindowResize(object? sender, EventArgs e)
    {
        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }
    
    private void InitializeRenderer(int realScreenWidth, int realScreenHeight)
    {
        _viewportManager.ScreenWidth = realScreenWidth;
        _viewportManager.ScreenHeight = realScreenHeight;

        // Maintain the virtual resolution regardless of window/fullscreen size
        _camera.VirtualWidth = VIRTUAL_WIDTH;
        _camera.VirtualHeight = VIRTUAL_HEIGHT;
        _camera.RecalculateTransformationMatrices();
    }
    
    protected override void Initialize()
    {
        // Allow for dynamic resolution detection
        var displayMode = GraphicsDevice.DisplayMode;

        // If in fullscreen mode, use the current display resolution
        if (_graphics.IsFullScreen)
        {
            _graphics.PreferredBackBufferWidth = displayMode.Width;
            _graphics.PreferredBackBufferHeight = displayMode.Height;
        }
        // Otherwise use appropriate windowed size based on screen resolution
        else
        {
            Window.AllowUserResizing = true;
            // For UHD screens, use a good windowed size
            if (displayMode.Width >= 3840)
            {
                _graphics.PreferredBackBufferWidth = Math.Min(2560, displayMode.Width - 200);
                _graphics.PreferredBackBufferHeight = Math.Min(1440, displayMode.Height - 200);
            }
            else if (displayMode.Width >= 2560)
            {
                _graphics.PreferredBackBufferWidth = Math.Min(1920, displayMode.Width - 200);
                _graphics.PreferredBackBufferHeight = Math.Min(1080, displayMode.Height - 200);
            }
        }

        _graphics.SynchronizeWithVerticalRetrace = true;
        _graphics.ApplyChanges();
        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        
        // Rest of initialization...
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        
        _spriteBatch = new(GraphicsDevice);
        _runner = new(1);
        _screenController = new(this, _runner, _viewportManager, _camera, _spriteBatch, Content);
        
        // Set camera position and calculate proper zoom
        _camera.Position = Vector2.Zero;
        _camera.Zoom = 2;

        // _screenController.RegisterScreen(ScreenName.Game, () => new DreamGameScreen(this, GraphicsDevice, Content, _camera, _renderer, _runner, _spriteBatch));
        // _screenController.RegisterScreen(ScreenName.Game, () => new DialogueExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        // _screenController.RegisterScreen(ScreenName.Game, () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.Game, () => new GameJamScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.MainMenu, () => new MainMenuScreen(GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.OptionsMenu, () => new OptionsMenuScreen(GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));

        _screenController.LoadScreen(ScreenName.Game);
        
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

    protected override void Dispose(bool disposing)
    {
        _screenController.Dispose();
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
        base.Dispose(disposing);
    }
}