using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System.Draw;

namespace MonoDreams.Demos.Web;

/// <summary>
/// The BlazorGL host for the MonoDreams module demos. Mirrors the desktop <c>Demos.Game1</c>
/// screen-assembly path but drops every desktop-only concern (headless capture/exit, OS window
/// position/resize events, the Q-to-quit shortcut). It registers the same six demo screens as
/// desktop and boots into the <see cref="DemoLauncherScreen"/> menu, so the browser flow matches
/// desktop (develop once, build everywhere).
///
/// GraphicsProfile is Reach (BlazorGL/WebGL); the Game loop is driven by the Blazor page
/// (requestAnimationFrame -> Tick), same as Examples.Web and the Phase 0 spike.
/// </summary>
public class WebGame : Game
{
    private const int VirtualWidth = 1280;
    private const int VirtualHeight = 720;

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private ViewportManager _viewportManager;
    private Camera _camera;
    private DefaultParallelRunner _runner = null!;
    private ScreenController _screenController = null!;

    public WebGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _graphics.GraphicsProfile = GraphicsProfile.Reach;
        // Do NOT force a back-buffer size: on BlazorGL the back buffer follows the canvas drawing
        // buffer (sized by the host page to the aspect-fit display size, 1:1). Forcing a size here
        // desyncs the render from the canvas (content offset/clipped). The per-frame viewport sync
        // keeps ScreenWidth matched to the actual back buffer.
        _graphics.ApplyChanges();

        _viewportManager = new ViewportManager(this, VirtualWidth, VirtualHeight);
        _camera = new Camera(VirtualWidth, VirtualHeight);
    }

    protected override void Initialize()
    {
        var debugDir = PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        Logger.Initialize(debugDir);
        Logger.Info("Demos WebGame initializing (BlazorGL).");

        _viewportManager.ScreenWidth = GraphicsDevice.Viewport.Width;
        _viewportManager.ScreenHeight = GraphicsDevice.Viewport.Height;
        _camera.RecalculateTransformationMatrices();

        // Project-wide dark navy theme for all MonoDreams demo screens (matches desktop Game1).
        FinalDrawSystem.ClearColor = DemoPalette.DarkBg;

        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;

        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _runner = new DefaultParallelRunner(1);
        _screenController = new ScreenController(this, _runner, _viewportManager, _camera, _spriteBatch, Content);

        // Same screen set as desktop Game1 — the platform is the only difference, the game flow
        // is identical.
        _screenController.RegisterScreen(DemoScreens.Launcher,
            () => new DemoLauncherScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));
        _screenController.RegisterScreen(DemoScreens.Camera,
            () => new MonoDreams.Demo.Camera.CameraDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));
        _screenController.RegisterScreen(DemoScreens.Physics,
            () => new MonoDreams.Demo.Physics.PhysicsDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, _runner));
        _screenController.RegisterScreen(DemoScreens.Dialogue,
            () => new MonoDreams.Demo.Dialogue.DialogueDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));
        _screenController.RegisterScreen(DemoScreens.Ui,
            () => new MonoDreams.Demo.Ui.UiDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));
        _screenController.RegisterScreen(DemoScreens.Audio,
            () => new MonoDreams.Demo.Audio.AudioDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));

        _screenController.LoadScreen(DemoScreens.Launcher);

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        // The browser canvas (and thus the GL backbuffer) is sized by the host page and can
        // change at any time; web has no Window.ClientSizeChanged event, so poll each frame and
        // re-sync the viewport (the desktop heads do this from the resize event).
        SyncViewportToBackbuffer();
        _screenController.Update(gameTime);
        base.Update(gameTime);
    }

    private void SyncViewportToBackbuffer()
    {
        var w = GraphicsDevice.Viewport.Width;
        var h = GraphicsDevice.Viewport.Height;
        if (w == _viewportManager.ScreenWidth && h == _viewportManager.ScreenHeight) return;

        _viewportManager.ScreenWidth = w;
        _viewportManager.ScreenHeight = h;
        _camera.RecalculateTransformationMatrices();
    }

    protected override void Draw(GameTime gameTime)
    {
        _screenController.Draw(gameTime);
        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        _screenController?.Dispose();
        Logger.Shutdown();
        _runner?.Dispose();
        _spriteBatch?.Dispose();
        _graphics?.Dispose();
        base.Dispose(disposing);
    }
}
