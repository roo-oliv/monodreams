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
using MonoDreams.System.Debug;
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

    private readonly HeadlessOptions _headless;
    private ScreenshotCaptureSystem? _screenshotCapture;
    private int _frame;

    public Game1(string[]? args = null)
    {
        _headless = HeadlessOptions.Parse(args);

        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        // Headless still renders at full virtual resolution into a real backbuffer —
        // the window is just hidden off-screen and never relied on for presentation.
        // The render path (and its memory behaviour) is exercised exactly as in a
        // visible run, which is the whole point: a 1×1 window would make the captured
        // frame meaningless. See issue #28 and the debug-module premises.
        _graphics.PreferredBackBufferWidth = VirtualWidth;
        _graphics.PreferredBackBufferHeight = VirtualHeight;
        if (_headless.Enabled)
        {
            _graphics.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = false;
        }
        else
        {
            _graphics.SynchronizeWithVerticalRetrace = true;
            IsFixedTimeStep = true;
        }
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
        var debugDir = global::System.Environment.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? global::System.IO.Path.Combine(global::System.AppDomain.CurrentDomain.BaseDirectory, "debug");
        Logger.Initialize(debugDir);

        // Project-wide dark navy theme for all MonoDreams demo screens.
        FinalDrawSystem.ClearColor = DemoPalette.DarkBg;

        if (_headless.Enabled)
        {
            // Hide the window off-screen; the GL context (and its backbuffer) stay live
            // so Draw renders real frames we read back to PNG.
            Window.Position = new Point(-2000, -2000);
            _screenshotCapture = new ScreenshotCaptureSystem(GraphicsDevice, captureIntervalSeconds: 0f, debugDir);
            Logger.Info($"Headless run: screen='{_headless.Screen}', frames={_headless.Frames}, " +
                        $"captureEvery={_headless.CaptureEvery}, sampleEvery={_headless.SampleEvery}.");
        }

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
        _screenController.RegisterScreen(DemoScreens.Physics,
            () => new MonoDreams.Demo.Physics.PhysicsDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, _runner));
        _screenController.RegisterScreen(DemoScreens.Dialogue,
            () => new MonoDreams.Demo.Dialogue.DialogueDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));
        _screenController.RegisterScreen(DemoScreens.Ui,
            () => new MonoDreams.Demo.Ui.UiDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch));

        // Headless jumps straight to the requested screen, skipping the launcher menu.
        _screenController.LoadScreen(_headless.Enabled ? _headless.Screen : DemoScreens.Launcher);

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        // Q exits the app from any screen; ESC is handled per-screen (typically
        // "back to launcher" inside a demo screen).
        if (!_headless.Enabled && Keyboard.GetState().IsKeyDown(Keys.Q))
            Exit();
        _screenController.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Headless deliberately does NOT early-return here (unlike the Examples host):
        // rendering every frame is what makes screenshots and render-path memory
        // behaviour observable.
        _screenController.Draw(gameTime);

        if (_headless.Enabled)
            DriveHeadless(gameTime);
    }

    /// Per-frame headless bookkeeping: heap sampling, screenshot capture, and auto-exit.
    /// Runs after the frame has been composited to the backbuffer so a capture reads a
    /// fully-rendered frame.
    private void DriveHeadless(GameTime gameTime)
    {
        var totalSeconds = (float)gameTime.TotalGameTime.TotalSeconds;
        var isFinalFrame = _frame >= _headless.Frames - 1;

        if (_headless.SampleEvery > 0 && _frame % _headless.SampleEvery == 0)
        {
            // Force a collection so the sample reflects the *live* (retained) managed
            // heap, not transient per-frame churn that the GC hasn't reclaimed yet.
            // A retained-object leak (e.g. the per-frame EntitySet leak from #27)
            // survives the collection and still shows growth, so the series stays
            // assertable as "flat over a static scene"; ordinary allocation churn is
            // collected away and doesn't masquerade as a leak.
            var heapBytes = global::System.GC.GetTotalMemory(forceFullCollection: true);
            Logger.Info($"Heap sample: frame={_frame} gt={totalSeconds:F2} bytes={heapBytes}");
        }

        var periodicCapture = _headless.CaptureEvery > 0 && _frame % _headless.CaptureEvery == 0;
        if (periodicCapture || isFinalFrame)
            _screenshotCapture?.CaptureNow(totalSeconds);

        _frame++;

        if (isFinalFrame)
        {
            Logger.Info($"Headless run complete after {_frame} frames. Exiting.");
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        _screenController.Dispose();
        _screenshotCapture?.Dispose();
        Logger.Shutdown();
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
        base.Dispose(disposing);
    }
}
