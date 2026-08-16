using System.Collections.Generic;
using System.Linq;
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
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Examples.Serialization;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Input;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System.Debug;

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
    private readonly bool _editor;
    // PS5 dev/import op: when set (via --export-scene <id> or MONODREAMS_EXPORT_SCENE), the head boots
    // headless, loads the given legacy level through the LDtk parser (the migration fallback,
    // still present when this op runs), imports the resulting world to a native .mdscene under the
    // resolved project source tree, and exits. This is how the committed migrated levels are generated;
    // it never runs in a normal launch (the field is null unless the flag is present).
    private readonly string _exportSceneId;
    private bool _exported;
    /// <summary>Where this run writes its log, replay plans and screenshots — resolved ONCE in the
    /// constructor (the logger has to be up before <see cref="WindowFit"/> logs its boot line) and
    /// reused by everything downstream.</summary>
    private readonly string _debugDir;
    /// <summary>Env-requested frame capture (<c>MONODREAMS_SCREENSHOT=png|raw</c>, optionally
    /// <c>MONODREAMS_SCREENSHOT_TARGET=Main|UI|HUD</c>) — the evidence channel for a WINDOWED run.
    /// Null unless the environment asked, and never built under <c>--headless</c>: this head's
    /// <see cref="Draw"/> early-returns there, so there is no frame to read. Reading a NAMED target
    /// is what keeps captured evidence comparable now that <see cref="WindowFit"/> makes the window
    /// size machine-dependent — the file geometry is the target's, not the window's.</summary>
    private ScreenshotCaptureSystem _envFrameCapture;
    /// <summary>The macOS power-management assertion (<c>MONODREAMS_KEEP_AWAKE=1</c>), held for the
    /// process lifetime — null unless the environment asked. A long replay run left alone is exactly
    /// what App Nap and display sleep suspend. See <c>MonoDreams.Debug.KeepAwake</c>.</summary>
    private global::System.IDisposable _keepAwake;
#if DEBUG
    private ImGuiRenderer _imGuiRenderer;
    private DebugInspector _debugInspector;
#endif

    public Game1(string[] args = null)
    {
        _headless = args?.Contains("--headless") ?? false;
        // The editor run configuration: `--editor` launch arg or MONODREAMS_EDITOR=1 env var
        // (both settable in a Rider run configuration) — THE way into the editor. When active,
        // every screen composes the editor overlay, the shell is always visible, and the
        // transport boots Paused (the toolbar's Play/Pause + Restart buttons drive the game).
        _editor = EditorRunFlag.IsEnabled(args, Environment.GetEnvironmentVariable);

        // PS5 headless import op (dev-only): "--export-scene <id>" or MONODREAMS_EXPORT_SCENE=<id>.
        _exportSceneId = ResolveExportSceneId(args, Environment.GetEnvironmentVariable);

        // Load settings first
        _settings = SettingsManager.Instance.Settings;

        // The logger comes up HERE, in the constructor, because the window-fit boot line below is
        // written from it — Logger writes before Initialize are silent no-ops (foundation premise
        // "Logger requires Initialize before any write"), which would make the one observable of the
        // window decision disappear. Initialize() reuses the same directory.
        _debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        Logger.Initialize(_debugDir);

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
            // A hidden, never-activated window makes Game.IsActive false, and MonoGame throttles
            // inactive games (InactiveSleepTime, 20ms/frame ≈ 50fps) — which would quietly break
            // the headless max-speed contract. Headless never sleeps on inactivity.
            InactiveSleepTime = TimeSpan.Zero;
            _graphics.ApplyChanges();
            // Headless deliberately opts OUT of WindowFit: the 1×1 off-screen window IS the contract
            // here (this head's Draw early-returns, so nothing is presented). Logged so the window
            // decision is in every run's log, whichever branch made it.
            Logger.Info("Headless run: window sizing (WindowFit) skipped — the 1x1 off-screen window is the contract.");
        }
        else
        {
            IsFixedTimeStep = true;
            _graphics.IsFullScreen = _settings.IsFullscreen;
            _graphics.SynchronizeWithVerticalRetrace = true;
#if MONODREAMS_WEB
            // The host page owns the canvas size on web — never set a backbuffer here.
            _graphics.ApplyChanges();
#else
            if (_settings.IsFullscreen)
            {
                // Fullscreen is not a window: there is nothing to fit inside, and the backbuffer IS
                // the mode the display is put into — so it stays the render resolution and the
                // presentation policy frames it. WindowFit owns the WINDOWED case only.
                _graphics.PreferredBackBufferWidth = _settings.VirtualWidth;
                _graphics.PreferredBackBufferHeight = _settings.VirtualHeight;
                _graphics.ApplyChanges();
                Logger.Info("Fullscreen run: window sizing (WindowFit) skipped — the backbuffer is the " +
                            $"render resolution {_settings.VirtualWidth}x{_settings.VirtualHeight}.");
            }
            else
            {
                // Open the LARGEST aspect-correct window that actually FITS the player's display,
                // capped at the render resolution — instead of pinning the backbuffer to it. Pinning
                // is the classic silent break this head used to ship: macOS does not clamp a FIXED
                // window, so a 1920x1080 backbuffer on a 1512x982-point laptop renders the bottom of
                // the menu (the Start buttons) below the physical screen, with no crash and no
                // warning. WindowFit applies the backbuffer and calls ApplyChanges itself, so nothing
                // may set PreferredBackBuffer* after it (foundation premise "WindowFit is opt-in, and
                // it is the ONLY thing allowed to size a game's window"). MONODREAMS_WINDOW=WxH
                // forces an exact size for scripted runs and screenshots; passing Window also turns
                // AllowUserResizing on (except under that override), which the ClientSizeChanged
                // handler below already feeds back into the ViewportManager.
                WindowFit.Apply(_graphics, _settings.VirtualWidth, _settings.VirtualHeight, Window);
            }
#endif
        }

        // Both coordinate spaces come from settings, and the camera comes from the ViewportManager —
        // so the authoring→render scale lives in exactly one place (rendering premise "Authoring space
        // and render space are distinct"). Layout 0 ⇒ single space (the shipped default).
        _viewportManager = new(this, _settings.VirtualWidth, _settings.VirtualHeight,
            _settings.LayoutWidth, _settings.LayoutHeight)
        {
            // The presentation dial, declared HERE even though the settings default is what the
            // engine would pick for a scaffolded game: how the frame reaches a window that is not the
            // render resolution is a game's decision, and now that WindowFit sizes that window to the
            // player's display it is a decision every run exercises. Same key, same resolution as the
            // web head (WebGame), so both heads present identically from one settings file.
            Policy = _settings.ResolvePresentation(),
        };
        _camera = _viewportManager.CreateCamera();
        // The two spaces this run is using, and the dial that frames them — the head's own
        // observable for "which resolution are these coordinates in?" (the Demos head logs the same).
        Logger.Info($"Render space: authoring={_viewportManager.LayoutWidth}x{_viewportManager.LayoutHeight}, " +
                    $"render={_viewportManager.VirtualWidth}x{_viewportManager.VirtualHeight}, " +
                    $"scale={_viewportManager.RenderScale:0.###}.");
        Logger.Info($"Presentation policy declared by the head: '{_settings.Presentation}' " +
                    "(GameSettings.Presentation → ViewportManager.Policy).");

        // Window resize handling is a desktop concern; a web head sizes the canvas
        // from the host page, so the OS-window event is gated out there.
#if !MONODREAMS_WEB
        Window.ClientSizeChanged += OnWindowResize;
        // AllowUserResizing is WindowFit's to set now (on for every fitted window — a resizable
        // window is the one macOS clamps for you — off only under MONODREAMS_WINDOW, which asked for
        // an exact size), so the editor no longer flips it separately. The resize path is unchanged:
        // OnWindowResize → ApplyEditorHiDpi/InitializeRenderer feeds the new device size into the
        // ViewportManager; EditorShellSystem relayouts the chrome + viewport inset on the dim/DPR
        // change and EditorChromeRenderSystem recreates the native Editor target at the new size.
        // Headless has a 1×1 off-screen window, never calls WindowFit, and stays non-resizable.
#endif
    }
    
    private void OnWindowResize(object sender, EventArgs e)
    {
        if (!ApplyEditorHiDpi())
            InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }

    /// <summary>
    /// Editor runs render at DEVICE resolution (macOS Retina: the stock DesktopGL backbuffer is
    /// logical-size and OS-upscaled ~2× — blurry chrome/overlays). Applied on the FIRST Update —
    /// not in Initialize, where MonoGame's window still has its pre-resize default size — and
    /// re-applied on every resize; returns whether the device-resolution path took over the
    /// renderer sizing.
    /// </summary>
    private bool ApplyEditorHiDpi()
    {
        if (!_editor || _headless) return false;
        var result = EditorHiDpi.TryEnable(this);
        if (!result.Applied) return false;
        _viewportManager.DevicePixelRatio = result.Scale;
        InitializeRenderer(result.Width, result.Height);
        return true;
    }
    
    private void InitializeRenderer(int realScreenWidth, int realScreenHeight)
    {
        _viewportManager.ScreenWidth = realScreenWidth;
        _viewportManager.ScreenHeight = realScreenHeight;
        _camera.RecalculateTransformationMatrices();
    }

    protected override void Initialize()
    {
        // The logger is already up (the constructor brought it up ahead of WindowFit's boot line);
        // this is the same directory every debug channel reads and writes.
        var debugDir = _debugDir;

        // Opt-in keep-awake, straight after the logger so its line lands in the run's log: an
        // unattended replay run is otherwise at the mercy of macOS App Nap and display sleep. No-op on
        // every other platform, and off unless asked.
        _keepAwake = MonoDreams.Debug.KeepAwake.FromEnvironment();

        if (_headless)
        {
            // Hiding the OS window is a desktop-only headless trick. SDL_HideWindow keeps the
            // window (and its GL context) alive but off the screen AND out of the click path —
            // macOS clamps off-screen positions back onto the display, so the position move alone
            // left visible, accidentally-clickable windows there. The move stays as the fallback
            // for a platform where the SDL hide is unavailable.
#if !MONODREAMS_WEB
            Window.Position = new Point(-2000, -2000);
            MonoDreams.Debug.HeadlessWindow.Hide(Window);
#endif
            Logger.Info("Running in headless mode.");
        }

        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        // Env-requested frame capture (MONODREAMS_SCREENSHOT[=png|raw], MONODREAMS_SCREENSHOT_TARGET,
        // …), owned end-to-end by ScreenshotCaptureSystem.FromEnvironment — never built headless,
        // where this head's Draw early-returns and there is no frame to read. Naming a target
        // (…_TARGET=Main) is how a windowed run produces evidence whose geometry is the target's
        // fixed resolution rather than whatever size WindowFit gave this machine's window.
#if !MONODREAMS_WEB
        if (!_headless) _envFrameCapture = ScreenshotCaptureSystem.FromEnvironment(GraphicsDevice, debugDir);
#endif

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

        // Resolve the versioned project (desktop-only, PS2): the env var MONODREAMS_PROJECT_ROOT or a
        // walk-up to game.mdproj. Handed to every screen's overlay so Save is gated on a resolved root
        // (unresolved ⇒ Save disabled with the "no project root" reason). Resolved once here — where
        // the editor flag is parsed — so the module stays game-agnostic. Null off the flag.
        // TD: pass this head's content-project dir name as the multi-manifest tie-break — the repo now also
        // holds MonoDreams.Demos/Content/game.mdproj (same depth as Examples' .Core manifest), so without
        // the hint the repo-root search would tie-break to Demos' manifest on ordinal (D < E).
        var projectContext = _editor ? EditorProjectContext.Resolve("MonoDreams.Examples.Core") : null;

        // TB-A: the host-scoped editor session — its viewport tab stack (the open scene/Game tabs + their
        // data snapshots) survives a screen switch, exactly like the shared GameState. Created once here
        // beside the ScreenController and passed to every editor-enabled screen like the project context;
        // each screen's overlay BINDS to it. Null off the flag.
        var session = _editor ? new EditorSession() : null;

        // Under the editor run flag EVERY screen composes the editor overlay (Wave 8a: the editor
        // is screen-agnostic — the menu and the runner are scenes like any level). The runner has
        // no cursor pipeline of its own, so its overlay brings one (provideCursorPipeline inside
        // the screen). The run flag is the ONLY way into the editor (transport model).
        // UX-C: each screen declares its editor-facing ScreenInfo — the Scenes panel reads which
        // configuration file a screen loads from. The menu + runner are bound to one scene id each; the
        // Game screen is the level-parameterized HOST (it loads whatever scene is requested), so every
        // .mdscene not claimed by a binding is listed under it. Explicit ids kill the pre-UX-C hazard
        // where all three screens defaulted to manifest.startScene and would save to the same file.
        _screenController.RegisterScreen(ScreenName.LevelSelection, () => new LevelSelectionScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch, editorEnabled: _editor, projectContext: projectContext, session: session),
            new ScreenInfo("Level Selection", LevelSelectionScreen.BoundSceneId));
        // In the export op the Game screen composes the LDtk import machinery (importMode); a
        // normal / editor boot composes native-only (the parsers are not wired to live game boot, PS5).
        var importMode = _exportSceneId != null;
        _screenController.RegisterScreen(ScreenName.Game, () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch, editorEnabled: _editor, projectContext: projectContext, importMode: importMode, session: session),
            new ScreenInfo("Game", BoundSceneId: null, HostsSceneFiles: true));
        _screenController.RegisterScreen(ScreenName.InfiniteRunner, () => new InfiniteRunnerScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch, editorEnabled: _editor, projectContext: projectContext, session: session),
            new ScreenInfo("Infinite Runner", InfiniteRunnerScreen.BoundSceneId));

        if (_editor)
        {
            // Boot the transport Paused (RunMode.Edit); the toolbar's Play/Pause + Restart
            // buttons drive it from here. GameState still CONSTRUCTS as Play — this is an
            // explicit host-level opt-in mutation, so unflagged runs are untouched.
            _screenController.State.RunMode = EditorRunFlag.InitialRunMode(true);
            Logger.Info("Editor run flag active (--editor / MONODREAMS_EDITOR=1): game screens compose the editor overlay; booting in Edit mode.");
        }

        // PS5 headless import op: load the legacy level (via the still-present LDtk fallback)
        // and, on the first Update, import the parsed world to a native .mdscene, then exit. Takes
        // precedence over the normal boot branches; only active when the export flag is set.
        if (_exportSceneId != null)
        {
            Logger.Info($"[export] Headless import op: loading legacy level '{_exportSceneId}' to migrate it to a native .mdscene.");
            // Boot the export in Edit so the gated logic group (physics/movement) freezes — the first
            // screen Update runs the screen's Load (which publishes LoadLevelRequest → the parser
            // populates the world) WITHOUT a game-logic tick perturbing the pristine parsed positions.
            _screenController.State.RunMode = EditorRunFlag.InitialRunMode(true);
            Services.AddService(new RequestedLevelComponent(_exportSceneId));
            _screenController.LoadScreen(ScreenName.Game);
            base.Initialize();
            return;
        }

        // Resolve the boot target first (replay plan > manifest startScene > menu), THEN decide
        // whether the splash screen fronts it. Replay plans are test timing contracts and headless/
        // editor/export runs are tools — none of them get the 1.5s brand hold; a normal interactive
        // boot shows the MonoDreams logo splash before its target screen.
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        string bootScreen;
        var showSplash = !_headless && !_editor;
        if (replayPlan?.StartScreen != null)
        {
            Logger.Info($"Replay plan detected. Skipping to screen '{replayPlan.StartScreen}'.");
            bootScreen = replayPlan.StartScreen;
            showSplash = false;
        }
        else if (replayPlan?.StartLevel != null)
        {
            Logger.Info($"Replay plan detected. Skipping to level '{replayPlan.StartLevel}'.");
            Services.AddService(new RequestedLevelComponent(replayPlan.StartLevel));
            bootScreen = ScreenName.Game;
            showSplash = false;
        }
        else if (ManifestBoot.ResolveStartScene(
                     ManifestBoot.TryReadManifest(Content.RootDirectory),
                     id => NativeLevelLoader.NativeSceneExists(Content.RootDirectory, id)) is { } startScene)
        {
            // PS4: the bundled game.mdproj (read via TitleContainer) names a startScene that resolves to a
            // bundled native .mdscene — boot it directly through the native-first LoadLevelRequest path
            // (the Game screen publishes LoadLevelRequest(startScene); LevelLoadRequestSystem resolves it
            // native-first). When the start scene has no native file yet (the Examples "island" placeholder
            // until PS5), ResolveStartScene returns null and the default menu boot below runs (back-compat).
            Logger.Info($"Manifest startScene '{startScene}' resolves to a native scene; booting it.");
            Services.AddService(new RequestedLevelComponent(startScene));
            bootScreen = ScreenName.Game;
        }
        else
        {
            bootScreen = ScreenName.LevelSelection;
        }

        if (showSplash)
        {
            var splashTarget = bootScreen;
            _screenController.RegisterScreen(ScreenName.Splash,
                () => new SplashScreen(GraphicsDevice, _viewportManager, _spriteBatch, splashTarget),
                new ScreenInfo("Splash"));
            _screenController.LoadScreen(ScreenName.Splash);
        }
        else
        {
            _screenController.LoadScreen(bootScreen);
        }

        base.Initialize();
    }

    private bool _hiDpiApplied;

    protected override void Update(GameTime gameTime)
    {
        // PS5 headless import op: the screen's Load (during Initialize) already published
        // LoadLevelRequest, so the LDtk parser + factories populated the world synchronously.
        // Import that pristine parsed world (before any game logic runs) to a native .mdscene and exit.
        if (_exportSceneId != null && !_exported)
        {
            _exported = true;
            // One screen Update finalizes the deferred screen swap and runs the screen's Load
            // (LoadScreen only queues the screen; Load — which publishes LoadLevelRequest and drives
            // the parser — runs on the next Update). Logic is frozen (Edit), so the world is pristine.
            _screenController.Update(gameTime);
            ExportSceneAndExit();
            return;
        }

        // First-frame HiDPI application (see ApplyEditorHiDpi): the OS window has its real size
        // only once the run loop starts, so Initialize is too early to measure it.
        if (!_hiDpiApplied)
        {
            _hiDpiApplied = true;
            ApplyEditorHiDpi();
        }

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

        // AFTER the composite: a capture reads the finished frame (or the pass's target, when one is
        // named), so it must follow the draw pipeline. Null unless the environment asked.
        _envFrameCapture?.Update(_screenController.State);

#if DEBUG
        _imGuiRenderer?.BeforeLayout(gameTime);
        _debugInspector?.Draw(_screenController.CurrentWorld);
        _imGuiRenderer?.AfterLayout();
#endif
    }

    /// <summary>Reads the PS5 export-op level id from <c>--export-scene &lt;id&gt;</c> or the
    /// <c>MONODREAMS_EXPORT_SCENE</c> env var; <c>null</c> (the normal case) disables the op.</summary>
    private static string ResolveExportSceneId(string[] args, Func<string, string> getEnv)
    {
        if (args != null)
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "--export-scene")
                    return args[i + 1];
        var env = getEnv?.Invoke("MONODREAMS_EXPORT_SCENE");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    /// <summary>Imports the current (freshly parsed) world to a native <c>.mdscene</c> under the
    /// resolved project source tree (<c>MONODREAMS_PROJECT_ROOT</c> → <c>Content/Levels</c>) and exits.
    /// The registry carries both engine and game serializers so every parsed component round-trips.</summary>
    private void ExportSceneAndExit()
    {
        var world = _screenController.CurrentWorld;

        // Screen infrastructure the level parse did NOT create: the DialogueSystem builds its dialogue
        // UI sub-graph at construction (identified by DialogueStateComponent — the same marker the
        // transport's KeepAlive uses). It is not level content, and on native boot the DialogueSystem
        // rebuilds it — so leaving it in the migrated scene would double-create the dialogue UI. Dispose
        // the sub-graph before importing so only parsed level content is captured.
        DisposeInfrastructureSubgraphs(world);

        var ctx = EditorProjectContext.Resolve("MonoDreams.Examples.Core");
        var levelsPath = ctx.Resolved
            ? ctx.LevelsPath
            : PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "Content", "Levels");
        var target = PlatformServices.Current.CombinePath(levelsPath, _exportSceneId + SceneWriter.SceneFileExtension);

        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        registry.RegisterGameComponents();
        var importer = new LevelImporter(new SceneWriter(new SceneSerializer(registry)));
        var written = importer.ImportToFile(world, target);
        Logger.Info($"[export] Imported level '{_exportSceneId}' to native scene '{written ?? "(refused)"}'.");
        Exit();
    }

    /// <summary>Disposes screen-infrastructure sub-graphs a system built (not the level parse), so they
    /// never land in a migrated scene. Today: the dialogue UI (root carries
    /// <see cref="MonoDreams.Dialogue.DialogueStateComponent"/>) plus its <c>ChildOf</c> descendants.</summary>
    private static void DisposeInfrastructureSubgraphs(DefaultEcs.World world)
    {
        var roots = new List<DefaultEcs.Entity>();
        using (var set = world.GetEntities().With<MonoDreams.Dialogue.DialogueStateComponent>().AsSet())
            roots.AddRange(set.GetEntities().ToArray());

        // Index children by parent so the descendant sweep is O(n).
        var childrenByParent = new Dictionary<DefaultEcs.Entity, List<DefaultEcs.Entity>>();
        using (var childSet = world.GetEntities().With<ChildOfComponent>().AsSet())
            foreach (var e in childSet.GetEntities())
            {
                var parent = e.Get<ChildOfComponent>().Parent;
                if (!parent.IsAlive) continue;
                (childrenByParent.TryGetValue(parent, out var list) ? list : childrenByParent[parent] = new List<DefaultEcs.Entity>()).Add(e);
            }

        var queue = new Queue<DefaultEcs.Entity>(roots);
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            if (childrenByParent.TryGetValue(e, out var kids))
                foreach (var k in kids) queue.Enqueue(k);
            if (e.IsAlive) e.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        _screenController.Dispose();
        // Before Logger.Shutdown: a raw run logs its byte/frame summary from Dispose.
        _envFrameCapture?.Dispose();
        // Likewise the keep-awake release line.
        _keepAwake?.Dispose();
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