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
        // The editor is a desktop authoring tool — let the designer resize the window like any IDE.
        // The resize path already exists (OnWindowResize → ApplyEditorHiDpi/InitializeRenderer feeds
        // the new device size into the ViewportManager; EditorShellSystem relayouts the chrome +
        // viewport inset on the dim/DPR change and EditorChromeRenderSystem recreates the native
        // Editor target at the new size). Shipped/non-editor runs keep the fixed window (match
        // existing behavior); headless has a 1×1 off-screen window and stays non-resizable.
        if (_editor && !_headless) Window.AllowUserResizing = true;
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
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        Logger.Initialize(debugDir);

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