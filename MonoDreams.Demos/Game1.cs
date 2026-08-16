using System.Globalization;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Debug;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;

namespace MonoDreams.Demos;

public class Game1 : Game
{
    // AUTHORING (layout) resolution — the space EVERY demo coordinate is written in. It never
    // changes with the render resolution: that is the whole point of the two-space model (rendering
    // premise "Authoring space and render space are distinct; the scale lives only in the cameras").
    private const int LayoutWidth = 1280;
    private const int LayoutHeight = 720;

    // RENDER (virtual) resolution — render targets + back buffer. Equal to the authoring size unless
    // MONODREAMS_RENDER_SCALE asks for more pixels, which is the reference "move the game to a higher
    // resolution" knob: no demo coordinate, UI number or test moves with it.
    private readonly int _virtualWidth;
    private readonly int _virtualHeight;
    private readonly float _renderScale;

    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private ViewportManager _viewportManager;
    private Camera _camera;
    private DefaultParallelRunner _runner = null!;
    private ScreenController _screenController = null!;

    private readonly HeadlessOptions _headless;
    private readonly bool _editor;
    private ScreenshotCaptureSystem? _screenshotCapture;

    /// <summary>
    /// The env-requested frame capture (<c>MONODREAMS_SCREENSHOT=raw|png</c>) — separate from
    /// <see cref="_screenshotCapture"/>, which is the headless host's own deterministic
    /// <c>CaptureNow</c> channel on chosen frames. This one is a per-frame take driven entirely by the
    /// environment, and it runs in BOTH headless and windowed runs so
    /// <c>MONODREAMS_SCREENSHOT=raw dotnet run --project MonoDreams.Demos …</c> yields a full-rate
    /// recording of whatever is on screen. Null unless the environment asked; see
    /// <see cref="ScreenshotCaptureSystem.FromEnvironment"/>, the single owner of that contract.
    /// </summary>
    private ScreenshotCaptureSystem? _envFrameCapture;

    /// <summary>
    /// The macOS power-management assertion (<c>MONODREAMS_KEEP_AWAKE=1</c>), held for the process
    /// lifetime — null unless the environment asked. An unattended run is exactly the run macOS App
    /// Nap and display sleep suspend, which shows up as a game that stops making progress rather than
    /// one that fails. See <see cref="KeepAwake"/>.
    /// </summary>
    private IDisposable? _keepAwake;

    private int _frame;
    private float _perfTimer;

    public Game1(string[]? args = null)
    {
        _headless = HeadlessOptions.Parse(args);
        // The editor run configuration: `--editor` launch arg or MONODREAMS_EDITOR=1 env var.
        // When active, every demo screen composes the editor overlay and the host boots straight
        // the transport Paused (RunMode.Edit). Honoured under --headless too: headless Demos renders
        // every frame (the observe-and-self-verify channel), so an editor-flagged headless run
        // captures the shell in its PNGs — the editor's own self-verification path. The flag-off
        // headless contract (HeadlessDemoTests) is untouched.
        _editor = EditorRunFlag.IsEnabled(args, Environment.GetEnvironmentVariable);

        // Opt-in render-resolution multiplier (MONODREAMS_RENDER_SCALE=1.5 → 1920x1080 render space
        // over the same 1280x720 authoring space). Unset/invalid ⇒ 1: authoring space IS render
        // space and every matrix, rectangle and mouse mapping is what it always was.
        _renderScale = ParseRenderScale(Environment.GetEnvironmentVariable("MONODREAMS_RENDER_SCALE"));
        _virtualWidth = (int)MathF.Round(LayoutWidth * _renderScale);
        _virtualHeight = (int)MathF.Round(LayoutHeight * _renderScale);

        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        // Platform-conditional: desktop GL supports HiDef; a web (BlazorGL/WebGL)
        // head defines MONODREAMS_WEB and falls back to Reach. Selected by the head.
#if MONODREAMS_WEB
        _graphics.GraphicsProfile = GraphicsProfile.Reach;
#else
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif
        // Headless still renders at full virtual resolution into a real backbuffer —
        // the window is just hidden off-screen and never relied on for presentation.
        // The render path (and its memory behaviour) is exercised exactly as in a
        // visible run, which is the whole point: a 1×1 window would make the captured
        // frame meaningless. See issue #28 and the debug-module premises.
        _graphics.PreferredBackBufferWidth = _virtualWidth;
        _graphics.PreferredBackBufferHeight = _virtualHeight;
        if (_headless.Enabled)
        {
            _graphics.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = false;
            // A hidden, never-activated window makes Game.IsActive false, and MonoGame throttles
            // inactive games (InactiveSleepTime, 20ms/frame ≈ 50fps) — which would quietly break
            // the headless max-speed contract. Headless never sleeps on inactivity.
            InactiveSleepTime = TimeSpan.Zero;
        }
        else
        {
            _graphics.SynchronizeWithVerticalRetrace = true;
            IsFixedTimeStep = true;
        }
        _graphics.ApplyChanges();

        // ONE construction site for both spaces: the ViewportManager owns them, and every camera in
        // the game comes out of it, so the authoring→render scale exists in exactly one place.
        _viewportManager = new ViewportManager(this, _virtualWidth, _virtualHeight, LayoutWidth, LayoutHeight);
        _camera = _viewportManager.CreateCamera();

        // OS-window resize is a desktop concern; a web head sizes from the host page.
#if !MONODREAMS_WEB
        Window.ClientSizeChanged += (_, _) =>
        {
            if (!ApplyEditorHiDpi())
                InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        };
        // Editor runs are user-resizable (like the Examples head); the resize handler above already
        // recomputes the renderer size and the shell/chrome relayout follows. Non-editor + headless
        // runs keep the fixed window.
        if (_editor && !_headless.Enabled) Window.AllowUserResizing = true;
#endif
    }

    /// <summary>Reads the render-scale knob: a positive float, else 1 (single-space).</summary>
    private static float ParseRenderScale(string? value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0f
            ? scale
            : 1f;

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

        // Opt-in keep-awake, straight after the logger so its own line lands in the run's log: a run
        // left alone for hours (the agentic case) is otherwise at the mercy of App Nap and display
        // sleep on macOS. No-op on every other platform, and off unless asked.
        _keepAwake = KeepAwake.FromEnvironment();

        // MONODREAMS_PROFILE=1 turns on per-system frame timing (SystemProfiler) — the way to find
        // out which system is eating the frame, identically on desktop and in the browser (where
        // the log reaches the dev console).
        SystemProfiler.Enabled =
            PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_PROFILE") == "1";
        if (SystemProfiler.Enabled)
            Logger.Info("[perf] per-system profiling ON (MONODREAMS_PROFILE=1).");

        // The two coordinate spaces this run is using — the feature's observable (and what the
        // render-scale regression test asserts on).
        Logger.Info(string.Format(CultureInfo.InvariantCulture,
            "Render space: authoring={0}x{1}, render={2}x{3}, scale={4:0.###}.",
            LayoutWidth, LayoutHeight, _virtualWidth, _virtualHeight, _viewportManager.RenderScale));

        // Project-wide dark navy theme for all MonoDreams demo screens.
        FinalDrawSystem.ClearColor = DemoPalette.DarkBg;

        if (_headless.Enabled)
        {
            // Hide the window; the GL context (and its full-res backbuffer) stay live so
            // Draw renders real frames we read back to PNG. SDL_HideWindow takes it off the
            // screen and out of the click path — macOS clamps the off-screen position move
            // back onto the display, so the move alone left a visible, clickable window
            // there; it stays as the fallback where the SDL hide is unavailable.
            // Desktop-only — a web head has no OS window to hide.
#if !MONODREAMS_WEB
            Window.Position = new Point(-2000, -2000);
            MonoDreams.Debug.HeadlessWindow.Hide(Window);
#endif
            _screenshotCapture = new ScreenshotCaptureSystem(GraphicsDevice, captureIntervalSeconds: 0f, debugDir);
            Logger.Info($"Headless run: screen='{_headless.Screen}', frames={_headless.Frames}, " +
                        $"captureEvery={_headless.CaptureEvery}, sampleEvery={_headless.SampleEvery}.");
        }

        // Env-requested frame capture, independent of --headless: a windowed run records the same way a
        // headless one does, which is the point (an agent captures headless, a human captures what they
        // are looking at). Desktop-only — a web head has no filesystem to dump 3.5 MiB a frame into, and
        // the construction itself mkdirs + logs, so it is not built there at all.
#if !MONODREAMS_WEB
        _envFrameCapture = ScreenshotCaptureSystem.FromEnvironment(GraphicsDevice, debugDir);
#endif

        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;

        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _runner = new DefaultParallelRunner(1);
        _screenController = new ScreenController(this, _runner, _viewportManager, _camera, _spriteBatch, Content);

        // TD: resolve the versioned project (desktop-only) under the flag so the Scenes panel lists the
        // demo scenes, Save has a root, and the universal palette composes. The multi-manifest tie-break
        // hint keeps a Demos-host resolve on MonoDreams.Demos/Content/game.mdproj — the repo also holds
        // Examples' manifest at the same depth (a bare shallowest-then-ordinal tie would pick it). Null off
        // the flag. (A co-located Demos run resolves via walk-up before the repo search; the hint is
        // defence-in-depth + what the pure disambiguation test asserts.)
        var projectContext = _editor ? EditorProjectContext.Resolve("MonoDreams.Demos") : null;

        // TB-A: the host-scoped editor session — its viewport tab stack survives a screen switch (the
        // launcher Play → Game tab following a transition to a demo screen). Seeded with the launcher's
        // bound scene id so the boot tab is NAMED (never "untitled"); the boot screen's Load corrects it
        // when a demo is booted directly (headless --screen). Null off the flag.
        var session = _editor ? new EditorSession(DemoLauncherScreen.BoundSceneId) : null;

        // Under the editor run flag EVERY demo screen composes the editor overlay (the editor is
        // host- and screen-agnostic — a demo is a scene like any level). Each screen brings its
        // own cursor pipeline, so the overlay never doubles it; keys come from the engine's
        // DefaultEditorKeys via the DemoEditor helper.
        // UX-C (TD): each demo screen declares its BOUND scene id — the demo selector itself is a scene too
        // (the launcher) — so the editor's Scenes panel lists the six demos as scenes and each Save targets
        // <id>.mdscene. The project context is handed to every screen so a demo scene can be saved/loaded.
        _screenController.RegisterScreen(DemoScreens.Launcher,
            () => new DemoLauncherScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, editorEnabled: _editor, session: session, projectContext: projectContext),
            new ScreenInfo("Launcher", DemoLauncherScreen.BoundSceneId));
        _screenController.RegisterScreen(DemoScreens.Camera,
            () => new MonoDreams.Demo.Camera.CameraDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, editorEnabled: _editor, session: session, projectContext: projectContext),
            new ScreenInfo("Camera Demo", MonoDreams.Demo.Camera.CameraDemoScreen.BoundSceneId));
        _screenController.RegisterScreen(DemoScreens.Physics,
            () => new MonoDreams.Demo.Physics.PhysicsDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, _runner, editorEnabled: _editor, session: session, projectContext: projectContext),
            new ScreenInfo("Physics Demo", MonoDreams.Demo.Physics.PhysicsDemoScreen.BoundSceneId));
        _screenController.RegisterScreen(DemoScreens.Dialogue,
            () => new MonoDreams.Demo.Dialogue.DialogueDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, editorEnabled: _editor, session: session, projectContext: projectContext),
            new ScreenInfo("Dialogue Demo", MonoDreams.Demo.Dialogue.DialogueDemoScreen.BoundSceneId));
        _screenController.RegisterScreen(DemoScreens.Ui,
            () => new MonoDreams.Demo.Ui.UiDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, editorEnabled: _editor, session: session, projectContext: projectContext),
            new ScreenInfo("UI Demo", MonoDreams.Demo.Ui.UiDemoScreen.BoundSceneId));
        _screenController.RegisterScreen(DemoScreens.Audio,
            () => new MonoDreams.Demo.Audio.AudioDemoScreen(GraphicsDevice, Content, _camera, _viewportManager, _spriteBatch, editorEnabled: _editor, session: session, projectContext: projectContext),
            new ScreenInfo("Audio Demo", MonoDreams.Demo.Audio.AudioDemoScreen.BoundSceneId));

        if (_editor)
        {
            // Boot the transport Paused (RunMode.Edit). GameState still CONSTRUCTS as Play —
            // this is an explicit host-level opt-in mutation, so unflagged runs are untouched.
            _screenController.State.RunMode = EditorRunFlag.InitialRunMode(true);
            Logger.Info("Editor run flag active (--editor / MONODREAMS_EDITOR=1): demo screens compose the editor overlay; booting in Edit mode.");
            if (_headless.Enabled)
                Logger.Info("Editor flag + --headless: the editor shell renders into the captured frames (observe-and-self-verify).");
        }

        // Headless jumps straight to the requested screen, skipping the launcher menu.
        _screenController.LoadScreen(_headless.Enabled ? _headless.Screen : DemoScreens.Launcher);

        base.Initialize();
    }

    private bool _hiDpiApplied;

    protected override void Update(GameTime gameTime)
    {
        // First-frame HiDPI application (see ApplyEditorHiDpi): the OS window has its real size
        // only once the run loop starts, so Initialize is too early to measure it.
        if (!_hiDpiApplied)
        {
            _hiDpiApplied = true;
            ApplyEditorHiDpi();
        }

        // Q exits the app from any screen; ESC is handled per-screen (typically
        // "back to launcher" inside a demo screen).
        if (!_headless.Enabled && Keyboard.GetState().IsKeyDown(Keys.Q))
            Exit();
        _screenController.Update(gameTime);

        if (SystemProfiler.Enabled)
        {
            SystemProfiler.CountFrame();
            SystemProfiler.ReportPeriodically(_screenController.State, ref _perfTimer);
        }
    }

    /// <summary>
    /// Editor runs render at DEVICE resolution (macOS Retina: the stock DesktopGL backbuffer is
    /// logical-size and OS-upscaled ~2× — blurry chrome/overlays). Applied on the first Update
    /// and re-applied on resize; returns whether the device-resolution path took over the
    /// renderer sizing. Headless runs keep the fixed virtual-size backbuffer (the capture
    /// contract).
    /// </summary>
    private bool ApplyEditorHiDpi()
    {
        if (!_editor || _headless.Enabled) return false;
        var result = EditorHiDpi.TryEnable(this);
        if (!result.Applied) return false;
        _viewportManager.DevicePixelRatio = result.Scale;
        InitializeRenderer(result.Width, result.Height);
        return true;
    }

    protected override void Draw(GameTime gameTime)
    {
        // Headless deliberately does NOT early-return here (unlike the Examples host):
        // rendering every frame is what makes screenshots and render-path memory
        // behaviour observable.
        _screenController.Draw(gameTime);

        // AFTER the composite — the backbuffer is what a capture reads, so this must follow the draw
        // pipeline exactly like DriveHeadless's CaptureNow does. Interval/format/cap all come from the
        // environment; a no-capture run has no instance here and pays a null check.
        _envFrameCapture?.Update(_screenController.State);

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
        // Before Logger.Shutdown: a raw run logs its byte/frame summary from Dispose.
        _envFrameCapture?.Dispose();
        // Likewise the keep-awake release line — and the assertion should end with the run, not linger
        // until the process is reaped.
        _keepAwake?.Dispose();
        Logger.Shutdown();
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
        base.Dispose(disposing);
    }
}
