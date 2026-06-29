using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Input;
using MonoDreams.Input;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Message;
using MonoDreams.Message.Level;
using MonoDreams.Message;
using MonoDreams.Platform;
using MonoDreams.Examples.Collision;
using MonoDreams.Examples.System;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.UI;
using MonoDreams.System;
using MonoDreams.System.Camera;
using MonoDreams.System.EntitySpawn;
using MonoDreams.Examples.EntityFactory;
using MonoDreams.Extension;
using MonoDreams.System.Physics;
using MonoDreams.System.Collision;
using MonoDreams.System.Cursor;
using MonoDreams.System.Debug;
using MonoDreams.Dialogue;
using MonoDreams.Examples.Settings;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.System.Draw;
using MonoDreams.Util;
using MonoDreams.System.Input;
using MonoDreams.System.Level;
using MonoDreams.Level;
using MonoDreams.Draw;
using MonoDreams.Examples.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// The reference in-game level-editor screen (Wave 4a). It composes the <b>real</b> game pipeline and
/// the editor systems in <b>one world</b>, per the "editor is part of the game" tenet — there is no
/// parallel editor renderer and no second data model. Entering Edit is a <see cref="GameState.RunMode"/>
/// flip (F1), <b>not</b> a screen swap: the world, camera and all state are preserved, the gated game
/// systems freeze, and the pre-registered editor systems wake up.
///
/// <para><b>Run-state policy (the §4 interaction matrix).</b> Game logic / physics / collision /
/// orb / NPC / dialogue and <c>CameraFollowSystem</c> are wrapped in
/// <c>GatedSystem(child, EditTimeBehavior.Freeze)</c> — they run in Play, freeze in Edit.
/// <c>HierarchySystem</c> stays <c>RunNormally</c> (NOT frozen) so an editor transform edit propagates
/// to world space the same frame. Cull / prep / sort / render and input / cursor stay live. The editor
/// systems (selection now; gizmo/toolbar in 4b) are registered RunNormally and Edit-guarded internally
/// (inert in Play).</para>
///
/// <para><b>Editor-overlay entities are standalone</b> — the editor creates no <c>ChildOf</c>-parented
/// overlay entities here, so <c>HierarchySystem.DisposeOrphans</c> (live in Edit) can't cascade-dispose
/// them. Deletion is the reversible <c>DeleteEntityCommand</c> (snapshots the sub-graph), never a bare
/// dispose.</para>
/// </summary>
public class LevelEditorScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly DrawLayerMap _layers;

    // Editor infrastructure (shared between the update + draw pipelines).
    private readonly ComponentSerializerRegistry _registry;
    private readonly SceneSerializer _sceneSerializer;
    private readonly EditorHistory _history;

    // The single gizmo-state entity: the toolbar's tool-select / snap-toggle mutate it; GizmoSystem
    // reads it. Created in CreateUpdateSystem so both the gizmo and the toolbar dispatch share it.
    private Entity _gizmoState;

    /// <summary>The default file name the toolbar's Save button writes to (under the host scene dir).</summary>
    private const string SceneFileName = "editor_scene.json";

    public LevelEditorScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) }
        };

        camera.Position = new Vector2(0, 0);

        _layers = DrawLayerMap.FromEnum<GameDrawLayer>()
            .WithYSort(GameDrawLayer.Characters);
        _world = new World();

        // The editor's serializer registry + bounded undo/redo history. The registry has the engine
        // serializers; a game registers its own game-component serializers here (none needed yet).
        _registry = new ComponentSerializerRegistry();
        _registry.RegisterEngineComponents();
        _sceneSerializer = new SceneSerializer(_registry);
        _history = new EditorHistory(_world);

        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public void Load(ScreenController screenController, ContentManager content)
    {
        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
        };
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        // Load the requested level so there is something to edit (same path as the play screen).
        var requestedLevel = screenController.Game.Services.GetService(typeof(RequestedLevelComponent)) as RequestedLevelComponent;
        if (requestedLevel != null)
        {
            _world.Publish(new LoadLevelRequest(requestedLevel.LevelIdentifier));
            screenController.Game.Services.RemoveService(typeof(RequestedLevelComponent));
        }
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var inputMappingSystem = new InputMappingSystem(_world);
        var actionMap = new Dictionary<string, AInputState>
        {
            ["Up"] = InputState.Up, ["Down"] = InputState.Down,
            ["Left"] = InputState.Left, ["Right"] = InputState.Right,
            ["Jump"] = InputState.Jump, ["Grab"] = InputState.Grab,
            ["Orb"] = InputState.Orb, ["Exit"] = InputState.Exit,
            ["Interact"] = InputState.Interact,
            ["Editor"] = InputState.Editor, ["Delete"] = InputState.Delete,
            ["Undo"] = InputState.Undo, ["Redo"] = InputState.Redo,
        };

        var replaySystem = InputReplaySystem.TryLoad(debugDir, actionMap, _game);

        // The headless editor-op channel (Wave 5): when an editor_op_plan.json is present, a scripted
        // cursor (no real mouse) drives the real editor systems. The driver injects CursorInputComponent
        // state that CursorInputSystem.SkipHardwareRead must leave untouched.
        var editorOpPlan = EditorOpPlan.TryLoad(debugDir);
        var cursorInputSystem = new CursorInputSystem(_world);

        ISystem<GameState> inputSystems;
        if (replaySystem != null)
        {
            inputMappingSystem.SkipHardwareRead = true;
            if (editorOpPlan != null) cursorInputSystem.SkipHardwareRead = true;
            inputSystems = new SequentialSystem<GameState>(
                cursorInputSystem, replaySystem, inputMappingSystem);
        }
        else if (editorOpPlan != null)
        {
            // Editor-op channel without a keyboard replay: still skip the hardware cursor read so the
            // injected state survives, and run input mapping normally (the channel drives the cursor).
            cursorInputSystem.SkipHardwareRead = true;
            inputSystems = new SequentialSystem<GameState>(cursorInputSystem, inputMappingSystem);
        }
        else
        {
            inputSystems = new ParallelSystem<GameState>(_parallelRunner,
                cursorInputSystem, inputMappingSystem);
        }

        var promptFont = _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");

        var blenderParser = new BlenderLevelParserSystem(_world, _content, _camera);
        blenderParser.SetDrawLayerMap(_layers);

        var entitySpawnSystem = new EntitySpawnSystem(_world, _content, _renderTargets);
        entitySpawnSystem.RegisterEntityFactory("Tile", new TileEntityFactory(_layers));
        entitySpawnSystem.RegisterEntityFactory("Wall", new WallEntityFactory(_content, _layers));
        entitySpawnSystem.RegisterEntityFactory("Player", new PlayerEntityFactory(_content, _layers));
        entitySpawnSystem.RegisterEntityFactory("Enemy", new NPCEntityFactory(_content, _layers));

        // Level loading + native-scene loading. SceneReaderSystem handles LoadSceneRequest (separate
        // from LoadLevelRequest) so the toolbar's Load (Wave 4b) reconstructs a native scene here.
        var levelLoadSystems = new SequentialSystem<GameState>(
            new LevelLoadRequestSystem(_world, _content),
            blenderParser,
            new LDtkTileParserSystem(_world, _content),
            new LDtkEntityParserSystem(_world),
            entitySpawnSystem,
            new SceneReaderSystem(_world, _sceneSerializer, _content));

        // Game logic + physics — FROZEN in Edit (the §4 matrix). Wrapping the whole block in one
        // Freeze gate is equivalent to wrapping each system: the gate skips the child Update entirely.
        var logicSystems = new GatedSystem(new SequentialSystem<GameState>(
            new MovementSystem(_world, _parallelRunner),
            new OrbSystem(_world),
            new StopMotionEffectSystem(_world),
            new TransformVelocitySystem(_world, _parallelRunner),
            new TransformCollisionDetectionSystem<CollisionMessage>(_world, GameCollisionHelper.Create),
            new TransformPhysicalCollisionResolutionSystem(_world),
            new TransformCommitSystem(_world, _parallelRunner),
            new TextUpdateSystem(_world),
            new NPCInteractionSystem(_world),
            new ZoneDialogueTriggerSystem(_world),
            new DialogueSystem(
                _world,
                _content.Load<Texture2D>("Dialouge UI/dialog box medium"),
                _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt"),
                _content.Load<Texture2D>("Dialouge UI/dialog box character finished talking click to continue indicator - spritesheet")
                    .Crop(new Rectangle(96, 0, 16, 16), _graphicsDevice),
                _viewportManager.VirtualWidth,
                _viewportManager.VirtualHeight,
                _layers.GetDepth(GameDrawLayer.DialogueUI),
                InputState.Interact,
                InputState.Up,
                InputState.Down,
                new[]
                {
                    _content.Load<YarnProgram>("Dialogues/hello_world"),
                    _content.Load<YarnProgram>("Dialogues/boldo")
                },
                nameof(EntityType.Interface))
        ), EditTimeBehavior.Freeze);

        // HierarchySystem stays LIVE in Edit (RunNormally) so editor transform edits propagate to
        // world space the same frame — explicitly NOT wrapped in a Freeze gate.
        var hierarchySystem = new HierarchySystem(_world);

        // Camera-follow FREEZES in Edit (the editor drives the camera); RunNormally in Play.
        var cameraFollowSystem = new GatedSystem(new CameraFollowSystem(_world, _camera), EditTimeBehavior.Freeze);

        var cursorLateUpdateSystem = new CursorPositionSystem(_world, _camera, _viewportManager);

        // Editor systems — pre-registered, RunNormally, Edit-guarded internally (inert in Play).
        var modeToggle = new EditorModeToggleSystem(state => InputState.Editor.JustPressed());
        var editorCommands = new EditorCommandSystem(
            _world, _history, _sceneSerializer,
            deleteRequested: _ => InputState.Delete.JustPressed(),
            undoRequested: _ => InputState.Undo.JustPressed(),
            redoRequested: _ => InputState.Redo.JustPressed());

        // The shared gizmo-state entity (single instance): the toolbar mutates it, GizmoSystem reads it.
        _gizmoState = _world.CreateEntity();
        _gizmoState.Set(GizmoStateComponent.Default);

        // The transform gizmo: reads SelectedComponent, draws handles + selection outline, drives a
        // drag into ONE undo step via the SAME _history (drag-coalescing). Runs BEFORE HierarchySystem
        // so an edit propagates to world space the same frame.
        var gizmoSystem = new GizmoSystem(_world, _camera, _history);

        // The engine-native toolbar (HUD target). Build the buttons, wire the dispatch to the screen's
        // own SAME _history / _sceneSerializer / _camera / _layers, and register the click system.
        new EditorToolbarBuilder(_world, promptFont).Build(viewportManager: _viewportManager);
        var toolbarSystem = new ToolbarSystem(_world, DispatchToolbarAction);
        var buttonMeshPrep = new ButtonMeshPrepSystem(_world);

        // The headless editor-op driver (Wave 5): scripted cursor + RunMode toggle + toolbar actions,
        // no real mouse. It is registered ONLY when a plan is present (zero cost in a normal run). It
        // runs AFTER CursorPositionSystem so its injected world/virtual cursor is the final word the
        // gizmo/toolbar read, and it holds the session open (requestExit fires only when its ops drain),
        // so the input-replay channel's auto-exit-on-drain can't kill the editor-op run early.
        EditorOpReplaySystem editorOpDriver = null;
        if (editorOpPlan != null)
        {
            editorOpDriver = new EditorOpReplaySystem(_world, editorOpPlan, DispatchToolbarAction, _game.Exit);
            // Hold the session open: if a keyboard replay is also present, suppress its auto-exit-on-drain
            // until the editor-op plan finishes, so the editor-op driver owns the exit.
            if (replaySystem != null)
                replaySystem.SuppressAutoExit = () => !editorOpDriver.IsComplete;
        }

        var systems = new List<ISystem<GameState>>
        {
            inputSystems,
            modeToggle,        // flips RunMode (works in both modes)
            levelLoadSystems,  // message-driven; loads game level + native scenes
            logicSystems,      // FROZEN in Edit
            editorCommands,    // delete / undo / redo (Edit-guarded)
            gizmoSystem,       // selection gizmo + drag → one undo step (Edit-guarded); BEFORE Hierarchy
            hierarchySystem,   // LIVE in Edit (propagates the gizmo edit this frame)
            cameraFollowSystem,// FROZEN in Edit
            buttonMeshPrep,    // toolbar button outline meshes (HUD) — rebuilt every frame
            toolbarSystem,     // toolbar clicks + visibility (AFTER buttonMeshPrep so Play-hide sticks)
            cursorLateUpdateSystem,
            new CursorDrawPrepSystem(_world),
        };
        if (editorOpDriver != null) systems.Add(editorOpDriver); // after cursor late-update: final word

        return new SequentialSystem<GameState>(systems.ToArray());
    }

    /// <summary>
    /// Wires a toolbar <see cref="EditorToolbarAction"/> to the screen's concrete behaviour, using the
    /// SAME shared instances the rest of the editor uses (no second <c>EditorHistory</c> / serializer).
    /// Tool-select + snap-toggle mutate the single <see cref="_gizmoState"/> entity that
    /// <c>GizmoSystem</c> reads; Save writes through <see cref="SceneWriter"/> with the live camera +
    /// layers; Load publishes a <see cref="LoadSceneRequest"/> the registered <c>SceneReaderSystem</c>
    /// handles; Undo/Redo drive the shared history.
    /// </summary>
    private void DispatchToolbarAction(EditorToolbarAction action)
    {
        switch (action)
        {
            case EditorToolbarAction.ToolMove: SetGizmoTool(GizmoTool.Move); break;
            case EditorToolbarAction.ToolRotate: SetGizmoTool(GizmoTool.Rotate); break;
            case EditorToolbarAction.ToolScale: SetGizmoTool(GizmoTool.Scale); break;
            case EditorToolbarAction.ToggleSnap: ToggleGizmoSnap(); break;
            case EditorToolbarAction.Save:
                new SceneWriter(_sceneSerializer).Save(_world, SceneFileName, _camera, _layers);
                break;
            case EditorToolbarAction.Load:
                _world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));
                break;
            case EditorToolbarAction.Undo: _history.Undo(); break;
            case EditorToolbarAction.Redo: _history.Redo(); break;
        }
    }

    private void SetGizmoTool(GizmoTool tool)
    {
        if (!_gizmoState.IsAlive) return;
        ref var state = ref _gizmoState.Get<GizmoStateComponent>();
        state.Tool = tool;
    }

    private void ToggleGizmoSnap()
    {
        if (!_gizmoState.IsAlive) return;
        ref var state = ref _gizmoState.Get<GizmoStateComponent>();
        state.SnapEnabled = !state.SnapEnabled;
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = SettingsManager.Instance.Settings.PixelPerfectRendering;

        var prepDrawSystems = new SequentialSystem<GameState>(
            new CullingSystem(_world, _camera),
            new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering),
            new YSortSystem(_world, _camera, _layers),
            new TextPrepSystem(_world, pixelPerfectRendering),
            new MeshPrepSystem(_world),
            new ColliderDebugSystem(_world),
            new SpriteDebugSystem(_world)
        );

        var mainPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera);
        var uiPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI]);
        var hudPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]);

        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, new[]
        {
            RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
            RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
            RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
        });

        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        var screenshotSystem = new ScreenshotCaptureSystem(_graphicsDevice, captureIntervalSeconds: 2.0f, debugDir)
        {
            IsEnabled = replayPlan?.Screenshots ?? false
        };

        // Selection runs at the END of the draw pipeline so it reads the FINAL post-YSort
        // DrawComponent.LayerDepth computed THIS frame (YSortSystem above ran first). The cursor's
        // click edge (set in the update phase) survives into the draw call, so picking is in-frame.
        var selectionSystem = new SelectionSystem(_world);

        return new SequentialSystem<GameState>(
            prepDrawSystems,
            selectionSystem,   // after YSort, before/independent of render; Edit-guarded
            mainPass,
            uiPass,
            hudPass,
            finalDrawToScreenSystem,
            screenshotSystem
        );
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}
