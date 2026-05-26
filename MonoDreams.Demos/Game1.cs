using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System.Draw;

namespace MonoDreams.Demos;

public class Game1 : Game
{
    private const int VirtualWidth = 1280;
    private const int VirtualHeight = 720;

    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private ViewportManager _viewportManager;
    private Camera _camera;
    private DefaultParallelRunner _runner = null!;
    private ScreenController _screenController = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _graphics.PreferredBackBufferWidth = VirtualWidth;
        _graphics.PreferredBackBufferHeight = VirtualHeight;
        _graphics.SynchronizeWithVerticalRetrace = true;
        IsFixedTimeStep = true;
        _graphics.ApplyChanges();

        _viewportManager = new ViewportManager(this, VirtualWidth, VirtualHeight);
        _camera = new Camera(VirtualWidth, VirtualHeight);

        Window.ClientSizeChanged += (_, _) => InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }

    private void InitializeRenderer(int realScreenWidth, int realScreenHeight)
    {
        _viewportManager.ScreenWidth = realScreenWidth;
        _viewportManager.ScreenHeight = realScreenHeight;
        _camera.RecalculateTransformationMatrices();
    }

    protected override void Initialize()
    {
        Logger.Initialize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug"));

        // Project-wide dark navy theme for all MonoDreams demo screens.
        FinalDrawSystem.ClearColor = SproutPalette.DarkBg;

        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;

        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _runner = new DefaultParallelRunner(1);
        _screenController = new ScreenController(this, _runner, _viewportManager, _camera, _spriteBatch, Content);

        _screenController.RegisterScreen(DemoScreens.Launcher,
            () => new DemoLauncherScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));
        _screenController.RegisterScreen(DemoScreens.Camera,
            () => new MonoDreams.Demo.Camera.CameraDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));

        _screenController.LoadScreen(DemoScreens.Launcher);

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        // Q exits the app from any screen; ESC is handled per-screen (typically
        // "back to launcher" inside a demo screen).
        if (Keyboard.GetState().IsKeyDown(Keys.Q))
            Exit();
        _screenController.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime) => _screenController.Draw(gameTime);

    protected override void Dispose(bool disposing)
    {
        _screenController.Dispose();
        Logger.Shutdown();
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
        base.Dispose(disposing);
    }
}
