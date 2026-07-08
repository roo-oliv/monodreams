using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
#if DEBUG && !MONODREAMS_WEB
using MonoDreams.Examples.Inspector;
#endif
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Runner;
using MonoDreams.Examples.System;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Message;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;
using MonoDreams.System.Input;
using MonoDreams.System.Physics;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// The infinite-runner reference game. Like every Examples screen since Wave 8a, its pipelines
/// are built through the <see cref="EditorPipelineRegistrar"/> and, when <c>editorEnabled</c> is
/// true (the <c>--editor</c> / <c>MONODREAMS_EDITOR=1</c> run flag), the <see cref="EditorOverlay"/>
/// is composed over this screen's own world — the editor is screen-agnostic. This screen runs
/// <b>no cursor pipeline of its own</b> (the runner is keyboard-only), so the overlay is asked to
/// provide one (<c>provideCursorPipeline: true</c>): its own <c>CursorInputSystem</c> /
/// <c>CursorPositionSystem</c> plus a minimal invisible cursor entity — that was this screen's
/// documented editor blocker. The whole runner logic block (movement, gravity, treadmill scroll,
/// spawner, collisions, off-screen cleanup, score) is <c>Freeze</c>-gated: those systems mutate
/// transforms every frame and would run entities out from under the gizmo in Edit. With the flag
/// off nothing editor-related is constructed and the pipeline is behaviourally identical.
///
/// <para>Note: the runner has no camera-follow — its camera is fixed at construction. In Edit the
/// camera-nav owns the camera (pan/zoom/frame); returning to Play resumes from wherever editing
/// left the camera (nothing re-pins it), which is the editor previewing exactly what changed.</para>
/// </summary>
public class InfiniteRunnerScreen : IGameScreen
{
    /// <summary>The scene id this screen is bound to (UX-C): its editor Save writes
    /// <c>infinite_runner.mdscene</c>, and its optional-scene-load brings that scene up under the
    /// code-built runner entities. Referenced by the host's <see cref="ScreenInfo"/>.</summary>
    public const string BoundSceneId = "infinite_runner";

    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ContentManager _content;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly MonoGame.Extended.BitmapFonts.BitmapFont _font;
    private readonly DrawLayerMap _layers;
#if DEBUG
    private InputMappingSystem _inputMappingSystem;
#endif

    // Wave 8a: the universal editor overlay (null when editorEnabled is false) and the retained
    // pipeline registries the systems panel binds to.
    private readonly bool _editorEnabled;
    private readonly EditorProjectContext? _projectContext;
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private EditorOverlay _editor;

    public InfiniteRunnerScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch,
        bool editorEnabled = false, EditorProjectContext? projectContext = null)
    {
        _game = game;
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _editorEnabled = editorEnabled;
        _projectContext = projectContext;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) }
        };

        _font = content.Load<MonoGame.Extended.BitmapFonts.BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");
        camera.Zoom = RunnerConstants.CameraZoom;
        camera.Position = RunnerConstants.CameraPosition;

        _layers = DrawLayerMap.FromEnum<RunnerDrawLayer>();
        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(InfiniteRunnerScreen), _updatePipeline, _drawPipeline);
        }
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public void Load(ScreenController screenController, ContentManager content)
    {
#if DEBUG && !MONODREAMS_WEB
        // Wire debug inspector input suppression (desktop-only ImGui tool).
        var debugInspector = screenController.Game.Services.GetService(typeof(DebugInspector)) as DebugInspector;
        if (debugInspector != null)
        {
            _inputMappingSystem.ShouldSuppressInput = () =>
                debugInspector.WantsKeyboard || (_editor != null && _editor.Dialog.IsOpen);
        }
#endif

        Logger.Info("Loading InfiniteRunner screen.");
        CreateTreadmill();
        CreatePlayer();
        CreateSpawnPoint();
        CreateScoreHUD(content);
        Logger.Info("InfiniteRunner screen loaded.");

        if (_editor != null)
        {
            // The transport's Restart re-runs exactly this load (the sweep disposed the runner
            // entities — treadmill, player, spawn point, HUD, and everything the spawner added). It must
            // ALSO re-run the optional scene load (UX-D) — otherwise a Restart (e.g. after Save Backup As)
            // rebuilds the code entities but drops the bound scene's placed content. Source-first via the
            // shared helper, so a backup-reload restores the last SAVE, not the last build.
            _editor.Transport.Reload = () =>
            {
                CreateTreadmill();
                CreatePlayer();
                CreateSpawnPoint();
                CreateScoreHUD(content);
                NativeLevelLoader.TryPublishSceneLoad(_world, _content.RootDirectory, BoundSceneId, _projectContext);
            };
            // Optional scene load (UX-C): bring infinite_runner.mdscene up under the code-built runner
            // if it exists (source-first, then bundled; absent → skip). The code entities stay.
            NativeLevelLoader.TryPublishSceneLoad(_world, _content.RootDirectory, BoundSceneId, _projectContext);
            // The Scenes panel + the dirty-gated switch (Examples hand-off).
            _editor.BindSceneCatalog(ScreenName.InfiniteRunner,
                () => screenController.RegisteredScreens,
                entry => EditorSceneSwitch.Switch(screenController, entry));
        }
    }

    private void CreateTreadmill()
    {
        // Invisible collider for physics
        var collider = _world.CreateEntity();
        collider.Set(new EntityInfoComponent("Wall"));
        collider.Set(new TransformComponent(new Vector2(0, RunnerConstants.TreadmillY)));
        collider.Set(new BoxColliderComponent(
            new Rectangle(0, 0, (int)RunnerConstants.TreadmillTotalWidth, (int)RunnerConstants.TreadmillSegmentHeight),
            passive: true));
        collider.Set(new RigidBodyComponent(isKinematic: true, gravityActive: false));

        // Cosmetic segments — top row (scrolls left)
        for (int i = 0; i < RunnerConstants.TreadmillSegmentCount; i++)
        {
            CreateTreadmillSegment(i, RunnerConstants.TreadmillY, RunnerConstants.TreadmillColor, isTopRow: true);
        }

        // Cosmetic segments — bottom row (scrolls right)
        var bottomY = RunnerConstants.TreadmillY + RunnerConstants.TreadmillSegmentHeight + RunnerConstants.BottomRowGap;
        for (int i = 0; i < RunnerConstants.TreadmillSegmentCount; i++)
        {
            CreateTreadmillSegment(i, bottomY, RunnerConstants.TreadmillBottomColor, isTopRow: false);
        }
    }

    private void CreateTreadmillSegment(int index, float y, Color color, bool isTopRow)
    {
        var x = index * (RunnerConstants.TreadmillSegmentWidth + RunnerConstants.TreadmillSegmentGap);
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfoComponent("Interface"));
        entity.Set(new TransformComponent(new Vector2(x, y)));

        var mesh = new FilledRectangleMeshGenerator(
            new Rectangle(0, 0, (int)RunnerConstants.TreadmillSegmentWidth, (int)RunnerConstants.TreadmillSegmentHeight),
            color).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = mesh.Vertices,
            Indices = mesh.Indices,
            PrimitiveType = mesh.PrimitiveType,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.Treadmill)
        });
        entity.Set(new VisibleComponent());
        entity.Set(new TreadmillSegment { IsTopRow = isTopRow });
    }

    private void CreateSpawnPoint()
    {
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfoComponent("Interface"));
        entity.Set(new TransformComponent(new Vector2(RunnerConstants.SpawnPointX, RunnerConstants.SpawnPointBaseY)));
        entity.Set(new SpawnPoint());

        var circleMesh = new CircleMeshGenerator(
            Vector2.Zero,
            RunnerConstants.SpawnPointRadius,
            RunnerConstants.SpawnPointColor,
            segments: 16).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = circleMesh.Vertices,
            Indices = circleMesh.Indices,
            PrimitiveType = circleMesh.PrimitiveType,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.SpawnPoint)
        });
        entity.Set(new VisibleComponent());
    }

    private void CreatePlayer()
    {
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfoComponent("Player"));
        entity.Set(new TransformComponent(RunnerConstants.PlayerStartPosition));
        entity.Set(new BoxColliderComponent(
            new Rectangle(
                RunnerConstants.PlayerColliderOffset.X,
                RunnerConstants.PlayerColliderOffset.Y,
                RunnerConstants.PlayerColliderSize.X,
                RunnerConstants.PlayerColliderSize.Y)));
        entity.Set(new RigidBodyComponent());
        entity.Set(new VelocityComponent());
        entity.Set(new RunnerState());

        var circleMesh = new CircleMeshGenerator(
            Vector2.Zero,
            RunnerConstants.PlayerRadius,
            RunnerConstants.PlayerColor,
            segments: 24).Generate();
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = circleMesh.Vertices,
            Indices = circleMesh.Indices,
            PrimitiveType = circleMesh.PrimitiveType,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.Player)
        });
        entity.Set(new VisibleComponent());
    }

    private void CreateScoreHUD(ContentManager content)
    {
        var entity = _world.CreateEntity();
        entity.Set(new EntityInfoComponent("Interface"));
        entity.Set(new TransformComponent(RunnerConstants.ScorePosition));
        entity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.HUD,
            LayerDepth = _layers.GetDepth(RunnerDrawLayer.HUD),
            TextContent = "Score: 0",
            Font = _font,
            Color = RunnerConstants.ScoreColor,
            Scale = RunnerConstants.ScoreTextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        entity.Set(new VisibleComponent());
        entity.Set(new ScoreDisplay());
    }

    private static CollisionMessage CreateRunnerCollision(
        Entity entity, Entity target, Vector2 contactPoint, Vector2 contactNormal, float contactTime, float penetrationDepth, int layer)
    {
        var entityType = entity.Get<EntityInfoComponent>().Type;
        var targetType = target.Get<EntityInfoComponent>().Type;
        var type = (entityType, targetType) switch
        {
            ("Player", "Collectible") => CollisionType.Collectible,
            ("Player", "Obstacle") => CollisionType.Damage,
            _ => CollisionType.Physics
        };
        return new CollisionMessage(entity, target, contactPoint, contactNormal, contactTime, penetrationDepth, layer, type);
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var inputMappingSystem = new InputMappingSystem(_world);
        // Modal capture (keyboard half): the editor/game keyboard (incl. Escape-to-exit) stands down
        // while a Save/Load dialog owns the keys (the closure reads the field lazily; _editor is set
        // just below when the editor is composed). The mouse half is the dialog consuming the cursor
        // edges. A DEBUG build's debug-inspector wiring in Load() re-combines this with WantsKeyboard.
        inputMappingSystem.ShouldSuppressInput = () => _editor != null && _editor.Dialog.IsOpen;
#if DEBUG
        _inputMappingSystem = inputMappingSystem;
#endif
        var actionMap = new Dictionary<string, AInputState>
        {
            ["Up"] = InputState.Up, ["Down"] = InputState.Down,
            ["Left"] = InputState.Left, ["Right"] = InputState.Right,
            ["Jump"] = InputState.Jump, ["Grab"] = InputState.Grab,
            ["Orb"] = InputState.Orb, ["Exit"] = InputState.Exit,
            ["Interact"] = InputState.Interact,
        };
        if (_editorEnabled)
        {
            // Editor replay-action names, mapped only when the overlay is composed so a plain
            // Play screen's replay surface is unchanged.
            actionMap["Delete"] = InputState.Delete;
            actionMap["Undo"] = InputState.Undo;
            actionMap["Redo"] = InputState.Redo;
            actionMap["Frame"] = InputState.Frame;
        }

        // The editor overlay (Wave 8a), built over THIS screen's world/camera/layers. The runner
        // has no cursor pipeline (keyboard-only game), so the overlay provides its own
        // (provideCursorPipeline: true) — cursor input/position systems + a minimal invisible
        // cursor entity. Chrome uses the same PPMondwest font as every other screen's shell.
        if (_editorEnabled)
        {
            var chromeFont = _content.Load<MonoGame.Extended.BitmapFonts.BitmapFont>("Fonts/PPMondwest-Regular-fnt");
            _editor = new EditorOverlay(
                _world, _camera, _layers, _content, chromeFont, _graphicsDevice, _spriteBatch,
                _viewportManager,
                new EditorInputBindings(
                    deleteRequested: _ => InputState.Delete.JustPressed(),
                    undoRequested: _ => InputState.Undo.JustPressed(),
                    redoRequested: _ => InputState.Redo.JustPressed(),
                    frameRequested: _ => InputState.Frame.JustPressed()),
                debugDir,
                requestExit: _game.Exit,
                setOsCursorVisible: visible => _game.IsMouseVisible = visible,
                provideCursorPipeline: true,
                sceneId: BoundSceneId, // explicit per-screen id (UX-C) — Save targets infinite_runner.mdscene
                projectContext: _projectContext);
            // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
            if (_editor.HasEditorOpPlan) _editor.CursorInput.SkipHardwareRead = true;
        }

        var replaySystem = InputReplaySystem.TryLoad(debugDir, actionMap, _game);

        if (replaySystem != null)
        {
            inputMappingSystem.SkipHardwareRead = true;
            // Hold the session open: a coexisting keyboard replay's auto-exit-on-drain defers to
            // the editor-op driver, which owns the exit.
            if (_editor?.SuppressReplayAutoExit != null)
                replaySystem.SuppressAutoExit = _editor.SuppressReplayAutoExit;
        }

        var entitySpawnSystem = new MonoDreams.System.EntitySpawn.EntitySpawnSystem(_world, null, _renderTargets);
        entitySpawnSystem.RegisterEntityFactory("Charm", new MonoDreams.Examples.EntityFactory.CharmFactory(_layers));
        entitySpawnSystem.RegisterEntityFactory("Obstacle", new MonoDreams.Examples.EntityFactory.ObstacleFactory(_layers));

        var hierarchySystem = new HierarchySystem(_world);

        // ---- Weave the update pipeline through the registrar. Composite blocks are registrar
        // GROUPS with named children (panel-visible). With the editor off, RunMode never leaves
        // Play and every gate is a pass-through, so the pipeline behaves exactly as the
        // pre-editor SequentialSystem(input, logic, hierarchy). ----
        var p = _updatePipeline;
        p.AddGroup("input", EditTimeBehavior.RunNormally, g =>
        {
            if (replaySystem != null) g.Add("replay", replaySystem);
            g.Add("mapping", inputMappingSystem);
        });
        if (_editor != null)
        {
            // The overlay-provided cursor pipeline (the runner has none): raw mouse state now,
            // world/virtual projection later (after camera-nav).
            p.Add("editor.cursorInput", _editor.CursorInput, EditTimeBehavior.RunNormally);
            p.Add("editor.sceneReader", _editor.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Dialog, EditTimeBehavior.RunNormally);
        }
        // The WHOLE runner simulation freezes in Edit: movement, gravity, treadmill scroll,
        // spawner, collisions, off-screen cleanup and score all mutate transforms/entities every
        // frame and would run the scene out from under the gizmo. One Freeze gate on the group.
        p.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("entitySpawn", entitySpawnSystem);
            g.Add("movement", new MonoDreams.Examples.System.Runner.RunnerMovementSystem(_world));
            g.Add("gravity", new GravitySystem(_world, _parallelRunner,
                RunnerConstants.WorldGravity, RunnerConstants.MaxFallVelocity));
            g.Add("treadmillScroll", new MonoDreams.Examples.System.Runner.TreadmillScrollSystem(_world));
            g.Add("spawner", new MonoDreams.Examples.System.Runner.RunnerSpawnerSystem(_world));
            g.Add("velocity", new TransformVelocitySystem(_world, _parallelRunner));
            g.Add("collisionDetect",
                new TransformCollisionDetectionSystem<CollisionMessage>(_world, CreateRunnerCollision));
            g.Add("collisionResolve", new TransformPhysicalCollisionResolutionSystem(_world));
            g.Add("collisionHandler", new MonoDreams.Examples.System.Runner.RunnerCollisionHandlerSystem(_world));
            g.Add("transformCommit", new TransformCommitSystem(_world, _parallelRunner));
            g.Add("gameOver", new MonoDreams.Examples.System.Runner.GameOverSystem(_world, _game, _font));
            g.Add("offScreenCleanup", new MonoDreams.Examples.System.Runner.OffScreenCleanupSystem(_world));
            g.Add("scoreDisplay", new MonoDreams.Examples.System.Runner.ScoreDisplaySystem(_world));
        });
        if (_editor != null)
        {
            p.Add("editor.commands", _editor.EditorCommands, EditTimeBehavior.RunNormally);
            p.Add("editor.gizmo", _editor.Gizmo, EditTimeBehavior.RunNormally);
            // The collider proxy sync follows the gizmo so the proxies re-derive from this
            // frame's write-back.
            p.Add("editor.proxySync", _editor.ProxySync, EditTimeBehavior.RunNormally);
        }
        p.Add("hierarchy", hierarchySystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.AddGroup("editor.toolbar", EditTimeBehavior.RunNormally, g =>
            {
                g.Add("meshPrep", _editor.ToolbarMeshPrep);
                g.Add("clicks", _editor.ToolbarClicks);
                g.Add("tooltip", _editor.Tooltip);
            });
            p.Add("editor.systemsPanel", _editor.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.inspector", _editor.Inspector, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.CameraNav, EditTimeBehavior.RunNormally);
            // The overlay's cursor projection — AFTER camera-nav so the camera mutation this
            // frame is what the cursor's world position derives from (no one-frame lag).
            p.Add("editor.cursorPosition", _editor.CursorPosition, EditTimeBehavior.RunNormally);
            p.Add("editor.shell", _editor.Shell, EditTimeBehavior.RunNormally);
            if (_editor.EditorOpDriver != null)
                p.Add("editor.opDriver", _editor.EditorOpDriver, EditTimeBehavior.RunNormally);
        }

        return p.Build();
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = MonoDreams.Examples.Settings.SettingsManager.Instance.Settings.PixelPerfectRendering;

        var mainPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera);
        var uiPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI]);
        var hudPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]);

        var renderLayers = new List<RenderLayer>
        {
            RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
            RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
            RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
        };
        if (_editor != null)
            renderLayers.Add(_editor.ChromeLayer);
        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, renderLayers);

        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        var screenshotSystem = new ScreenshotCaptureSystem(_graphicsDevice, captureIntervalSeconds: 2.0f, debugDir)
        {
            IsEnabled = replayPlan?.Screenshots ?? false
        };

        // ---- Weave the draw pipeline through the registrar (retained for the systems panel). ----
        var p = _drawPipeline;
        // The runner's own content is meshes + HUD text. With the editor composed, the sprite
        // prep chain (cull → sprite prep → Y-sort) is added so a native scene loaded/pasted while
        // editing actually previews (self-sufficient overlay); the runner's DrawLayerMap has no
        // Y-sorted layer, so YSortSystem passes depths through and selection picks on the final
        // (source-derived) LayerDepth — documented degradation.
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            if (_editorEnabled)
            {
                g.Add("culling", new CullingSystem(_world, _camera));
                g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering));
                g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            }
            g.Add("meshPrep", new MeshPrepSystem(_world));
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering));
        });
        if (_editor != null)
        {
            p.Add("editor.selection", _editor.Selection, EditTimeBehavior.RunNormally);
            // The overlay visuals (gizmo handles / selection outline / proxy outlines) bake in
            // screen pixels on the Editor target from the frame's FINAL camera + selection.
            p.Add("editor.overlayPrep", _editor.OverlayPrep, EditTimeBehavior.RunNormally);
        }
        p.Add("renderMain", mainPass, EditTimeBehavior.RunNormally);
        p.Add("renderUI", uiPass, EditTimeBehavior.RunNormally);
        p.Add("renderHUD", hudPass, EditTimeBehavior.RunNormally);
        if (_editor != null)
            p.Add("editor.renderChrome", _editor.ChromeRender, EditTimeBehavior.RunNormally);
        p.Add("finalDraw", finalDrawToScreenSystem, EditTimeBehavior.RunNormally);
        p.Add("screenshots", screenshotSystem, EditTimeBehavior.RunNormally);

        return p.Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values)
        {
            rt.Dispose();
        }
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    public enum RunnerDrawLayer
    {
        HUD,         // front - score display
        Player,      // player circle
        Collectible, // charms
        Obstacle,    // obstacles
        Treadmill,   // treadmill segments
        SpawnPoint,  // spawn point indicator (back)
    }
}
