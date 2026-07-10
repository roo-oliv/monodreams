using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
#if DEBUG && !MONODREAMS_WEB
using MonoDreams.Examples.Inspector;
#endif
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Input;
using MonoDreams.Input;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Message;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Examples.Serialization;
using MonoDreams.Message.Level;
using MonoDreams.Message;
using MonoDreams.Platform;
using MonoDreams.Examples.Collision;
using MonoDreams.Examples.System;
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
using MonoDreams.Draw;
using MonoDreams.Examples.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// The reference platformer screen (LDtk levels + native scenes) — and, since Wave 6, the single
/// composition path for the in-game level editor. The pipeline is built through an
/// <see cref="EditorPipelineRegistrar"/> (every entry named + wrapped in a run-state gate with
/// the §4-matrix policy: game logic / physics / dialogue and camera-follow <c>Freeze</c> in Edit,
/// everything else <c>RunNormally</c>), and when <c>editorEnabled</c> is true the
/// <see cref="EditorOverlay"/>'s systems are woven in at their documented points. With the flag
/// off nothing editor-related is constructed and — because <c>RunMode</c> never leaves Play and a
/// <c>RunNormally</c>/<c>Freeze</c> gate is a pass-through in Play — the screen behaves exactly
/// as it did before the editor existed.
///
/// <para>The editor arrives ONE way: the <c>--editor</c> / <c>MONODREAMS_EDITOR=1</c> run
/// configuration (see <see cref="EditorRunFlag"/>) makes the host register every screen with the
/// overlay and boot the transport Paused (<see cref="RunMode.Edit"/>). The editor is then always
/// visible; the toolbar's Play/Pause + Restart buttons (<see cref="EditorTransport"/>) drive the
/// game — Restart re-publishes the level recorded in <c>Load</c> and discards unsaved edits.</para>
///
/// <para>The retained registrars (<see cref="EditorPipelineRegistrar"/>) are bound onto the
/// overlay (<c>BindPipelines</c>) — the seam the editor's systems panel will enumerate and
/// toggle.</para>
/// </summary>
public class LoadLevelExampleGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
#if DEBUG
    private InputMappingSystem _inputMappingSystem;
#endif
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly DrawLayerMap _layers;

    // Wave 6: the editor overlay (null when editorEnabled is false) and the retained pipeline
    // registries the systems panel binds to.
    private readonly bool _editorEnabled;
    private readonly bool _importMode;
    private readonly EditorProjectContext? _projectContext;
    private readonly EditorSession _session;
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private EditorOverlay _editor;

    public LoadLevelExampleGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch,
        bool editorEnabled = false, EditorProjectContext? projectContext = null, bool importMode = false,
        EditorSession session = null)
    {
        _game = game;
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _editorEnabled = editorEnabled;
        _importMode = importMode;
        _projectContext = projectContext;
        _session = session;
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
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(LoadLevelExampleGameScreen), _updatePipeline, _drawPipeline);
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
                debugInspector.WantsKeyboard || (_editor != null && (_editor.Dialog.IsOpen || _editor.Menu.IsOpen || _editor.Modal.IsActive || _editor.InspectorOwnsKeyboard));
        }
#endif

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
            // Add more cursor types as needed
        };

        // Create cursor entity
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        // Check if a level was requested from the level selection screen
        var requestedLevel = screenController.Game.Services.GetService(typeof(RequestedLevelComponent)) as RequestedLevelComponent;
        if (requestedLevel != null)
        {
            // Load the requested level
            var levelId = requestedLevel.LevelIdentifier;
            // TB-A: when a cross-screen scene tab activation is in flight, the editor restores that tab's
            // in-memory snapshot through the reader instead — so SKIP the disk load (no double content,
            // pre-mortem #2). A plain boot / a Game tab riding a gameplay transition has no pending
            // activation, so this returns false and the fresh disk load runs (RunMode untouched → stays
            // Playing when the Game tab follows gameplay, pre-mortem #3).
            if (_editor == null || !_editor.RestorePendingActivation(screenController.State))
                _world.Publish(new LoadLevelRequest(levelId));

            if (_editor != null)
            {
                // The Game screen is the level-parameterized HOST: its scene id is the level it was
                // asked to load (UX-C) — so Save targets <levelId>.mdscene, not the manifest default.
                _editor.SetSceneId(levelId);
                // The transport's Restart re-publishes this exact load request (the screen records
                // what it loaded); the transport clears CurrentLevelComponent + disposes the scene
                // entities first, so the parsers re-parse from scratch. Unsaved edits are discarded.
                _editor.Transport.Reload = () => _world.Publish(new LoadLevelRequest(levelId));
            }

            // Remove the service so it doesn't interfere with future screen loads
            screenController.Game.Services.RemoveService(typeof(RequestedLevelComponent));
        }

        if (_editor != null)
        {
            // Screen infrastructure the restart sweep must keep: DialogueSystem creates its UI
            // sub-graph once at construction and holds it by reference — its root carries
            // DialogueStateComponent, and keeps propagate to ChildOf descendants.
            _editor.Transport.KeepAlive = e => e.Has<MonoDreams.Dialogue.DialogueStateComponent>();
            // The Scenes panel + the dirty-gated switch (Examples hand-off). The Game screen hosts
            // every unclaimed .mdscene, so the Scenes list is richest here.
            _editor.BindSceneCatalog(ScreenName.Game,
                () => screenController.RegisteredScreens,
                entry => EditorSceneSwitch.Switch(screenController, entry));
        }
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var inputMappingSystem = new InputMappingSystem(_world);
        // Modal capture (keyboard half): the editor/game keyboard (incl. Escape-to-exit) stands down
        // while a Save/Load dialog owns the keys (the closure reads the field lazily; _editor is set
        // below when the editor is composed). The mouse half is the dialog consuming the cursor edges.
        // A DEBUG build's debug-inspector wiring in Load() re-combines this with WantsKeyboard.
        inputMappingSystem.ShouldSuppressInput = () =>
            _editor != null && (_editor.Dialog.IsOpen || _editor.Menu.IsOpen || _editor.Modal.IsActive || _editor.InspectorOwnsKeyboard);
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
            // Play screen's replay surface is unchanged. Delete/Undo/Redo/Frame are NOT here: they
            // are chord shortcuts (EditorShortcuts, UX3-E), and input replay synthesizes AInputState
            // actions — not raw chords — so chord-driven editing is exercised through the editor op
            // channel (menu:*/view:frame/toolbar Undo/Redo), never replay (the chord replay caveat).
            // The tool-contextual ghost-rotate keys stay replayable.
            actionMap["RotateCw"] = InputState.RotateCw;
            actionMap["RotateCcw"] = InputState.RotateCcw;
        }

        var promptFont = _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");

        // The editor overlay: the shared registry/serializer/history, every editor system, the
        // HUD toolbar, and the headless op channel — built over THIS screen's world/camera/layers
        // (the editor is part of the game). Constructed before the input composition because the
        // presence of a headless editor-op plan changes the cursor-input wiring below.
        if (_editorEnabled)
        {
            // The asset palette's inputs are SCREEN-supplied (the level-editor module stays
            // game-agnostic): the catalog scans the gitignored Content/Island/ drop folder (see
            // its committed MANIFEST.md — the desktop head copies it raw to the output Content
            // dir, no MGCB round-trip), and the band map projects the palette's layer selector
            // onto THIS game's DrawLayerMap (Props = the Y-sorted Characters band → placed props
            // get the feet-origin convention automatically).
            var islandRoot = PlatformServices.Current.CombinePath(
                PlatformServices.Current.BaseDirectory, _content.RootDirectory, "Island");
            var assetCatalog = MonoDreams.LevelEditor.Assets.AssetCatalog.Scan(islandRoot, "Island");
            var paletteBands = new[]
            {
                new MonoDreams.LevelEditor.Assets.PaletteBand("Ground", _layers.GetDepth(GameDrawLayer.Background), YSorted: false),
                new MonoDreams.LevelEditor.Assets.PaletteBand("Detail", _layers.GetDepth(GameDrawLayer.Tiles), YSorted: false),
                new MonoDreams.LevelEditor.Assets.PaletteBand("Props", _layers.GetDepth(GameDrawLayer.Characters), YSorted: true),
                new MonoDreams.LevelEditor.Assets.PaletteBand("Overhead", _layers.GetDepth(GameDrawLayer.Foreground), YSorted: false),
            };

            // The trigger-zone types this game offers (island-authoring §5.3) — screen-supplied, so
            // the level-editor module stays game-agnostic. Placing one drops a Passive box collider
            // whose EntityInfoComponent identity (Type = prefix, Name = "<prefix>_NN") a game
            // reaction system pattern-matches on (the ZoneDialogueTriggerSystem precedent).
            var triggerTypes = new[]
            {
                new MonoDreams.LevelEditor.Assets.TriggerType("evidence", "Evidence"),
                new MonoDreams.LevelEditor.Assets.TriggerType("talkzone", "TalkZone"),
                new MonoDreams.LevelEditor.Assets.TriggerType("exit", "Exit"),
            };

            _editor = new EditorOverlay(
                _world, _camera, _layers, _content, promptFont, _graphicsDevice, _spriteBatch,
                _viewportManager,
                new EditorInputBindings(
                    // Delete / frame / undo / redo are the consolidated EditorShortcuts chord table
                    // (UX3-E), read off the raw keyboard — not wired here anymore. Only the
                    // tool-contextual keys remain game-supplied:
                    // Escape (already mapped to InputState.Exit; nothing else consumes it on this
                    // screen) disarms the palette's Place mode AND cancels a boundary lay.
                    cancelRequested: _ => InputState.Exit.JustPressed(),
                    // Q/E rotate the armed palette ghost before stamping (road pieces / props).
                    rotateCwRequested: _ => InputState.RotateCw.JustPressed(),
                    rotateCcwRequested: _ => InputState.RotateCcw.JustPressed()),
                debugDir,
                requestExit: _game.Exit,
                // The shell shows the OS cursor over the chrome margins while editing.
                setOsCursorVisible: visible => _game.IsMouseVisible = visible,
                assetCatalog: assetCatalog,
                paletteBands: paletteBands,
                triggerTypes: triggerTypes,
                projectContext: _projectContext,
                session: _session);

            // Game-component serializers (PS5): register the reference game's own component
            // serializers onto the editor's live registry so the in-editor Load/Save round-trips
            // PlayerState / OrbitalMotion / StopMotionEffect / DialogueZoneComponent. The engine
            // serializers are already registered by the overlay's ctor; this adds the game's on top.
            _editor.Registry.RegisterGameComponents();
        }

        var replaySystem = InputReplaySystem.TryLoad(debugDir, actionMap, _game);
        var cursorInputSystem = new CursorInputSystem(_world, _viewportManager);
        var editorOpActive = _editor?.HasEditorOpPlan == true;

        // The input block is registered as a registrar GROUP below (children visible in the
        // systems panel); only the composite KIND depends on the run configuration. Historical
        // shapes preserved exactly: replay = Sequential(cursor, replay, mapping); editor-op
        // without replay = Sequential(cursor, mapping); plain run = Parallel(cursor, mapping).
        var inputKind = PipelineCompositeKind.Parallel;
        if (replaySystem != null)
        {
            inputMappingSystem.SkipHardwareRead = true;
            // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
            if (editorOpActive) cursorInputSystem.SkipHardwareRead = true;
            inputKind = PipelineCompositeKind.Sequential;
            // Hold the session open: a coexisting keyboard replay's auto-exit-on-drain defers to
            // the editor-op driver, which owns the exit.
            if (_editor?.SuppressReplayAutoExit != null)
                replaySystem.SuppressAutoExit = _editor.SuppressReplayAutoExit;
        }
        else if (editorOpActive)
        {
            // Editor-op channel without a keyboard replay: still skip the hardware cursor read so
            // the injected state survives, and run input mapping normally.
            cursorInputSystem.SkipHardwareRead = true;
            inputKind = PipelineCompositeKind.Sequential;
        }

        // Import machinery (PS5): the LDtk/Blender parsers + spawn factories are composed ONLY in
        // import mode (the dev/export op that re-parses a legacy level so the importer can serialize
        // it to a native .mdscene). The shipped game/editor boot is native-only — these never run at
        // live boot, closing the parser-asymmetry. Constructing the factories here also loads their
        // textures, so gating construction keeps a normal boot from touching legacy tilesets.
        EntitySpawnSystem entitySpawnSystem = null;
        if (_importMode)
        {

        entitySpawnSystem = new EntitySpawnSystem(_world, _content, _renderTargets);
        entitySpawnSystem.RegisterEntityFactory("Tile", new TileEntityFactory(_layers));
        entitySpawnSystem.RegisterEntityFactory("Wall", new WallEntityFactory(_content, _layers));
        entitySpawnSystem.RegisterEntityFactory("Player", new PlayerEntityFactory(_content, _layers));
        entitySpawnSystem.RegisterEntityFactory("Enemy", new NPCEntityFactory(_content, _layers));

        // Prefab spawn channel (PF-C): "prefab:<id>" spawns a full linked instance through the ONE
        // PrefabExpander. With the editor composed, reuse its expander (source-first resolution + the
        // shared registry that already has the game serializers — see _editor.Registry.RegisterGameComponents
        // above). Shipped (no editor): build a bundled-only (TitleContainer) expander over engine + game
        // serializers so a shipped game can spawn prefabs via EntitySpawnRequest("prefab:<id>", pos) too.
        if (_editor != null)
        {
            entitySpawnSystem.RegisterEntityFactoryPrefix(
                MonoDreams.LevelEditor.EntityFactory.PrefabFactory.IdentifierPrefix, _editor.PrefabFactory);
        }
        else
        {
            var prefabRegistry = new ComponentSerializerRegistry();
            prefabRegistry.RegisterEngineComponents();
            prefabRegistry.RegisterGameComponents();
            var prefabExpander = new PrefabExpander(
                new SceneSerializer(prefabRegistry),
                new PrefabFileSource(_content.RootDirectory, _projectContext).Resolve,
                loadTexture: key => _content.Load<Texture2D>(key),
                fileTextureLoader: new FileAssetTextureLoader(_graphicsDevice, _content.RootDirectory).Load);
            entitySpawnSystem.RegisterEntityFactoryPrefix(
                MonoDreams.LevelEditor.EntityFactory.PrefabFactory.IdentifierPrefix,
                new MonoDreams.LevelEditor.EntityFactory.PrefabFactory(prefabExpander));
        }
        } // end if (_importMode)

        // Hierarchy system must run AFTER logic systems modify transforms
        // but BEFORE any systems read world transforms (camera, rendering, etc.)
        var hierarchySystem = new HierarchySystem(_world);

        var cameraFollowSystem = new CameraFollowSystem(_world, _camera);

        // Cursor position must update AFTER camera has moved to avoid 1-frame lag
        var cursorLateUpdateSystem = new CursorPositionSystem(_world, _camera, _viewportManager);

        // ---- Weave the update pipeline through the registrar (the §4 interaction matrix). ----
        // Every entry is gate-wrapped by name+policy; composite blocks are registrar GROUPS with
        // named children (the registrar builds the composite), so the systems panel sees and
        // toggles every system. With the editor off, RunMode never leaves Play and every gate is
        // a pass-through, so the pipeline behaves exactly as before.
        // Native-first level loading (PS4): the game boots bundled .mdscene levels through the native
        // reader. The reader is composed once — reuse the editor overlay's when present (else build a
        // standalone one so a SHIPPED game with no editor still boots native scenes; behaviorally inert
        // for a legacy LDtk level, which has no .mdscene and falls through). The probe is
        // handed to LevelLoadRequestSystem: on a LoadLevelRequest whose id has a bundled
        // Content/Levels/<id>.mdscene, it publishes a LoadSceneRequest (handled synchronously by the
        // reader) and the LDtk path is skipped. No native file ⇒ the legacy LDtk fallback runs unchanged
        // (migration coexistence — removed in PS5).
        ISystem<GameState> nativeSceneReader;
        if (_editor != null)
        {
            nativeSceneReader = _editor.SceneReader;
        }
        else
        {
            var nativeRegistry = new ComponentSerializerRegistry();
            nativeRegistry.RegisterEngineComponents();
            // Game-component serializers on the SHIPPED (no-editor) reader (PS5 handoff): a booted
            // native scene migrated from a legacy LDtk level carries game components (PlayerState,
            // StopMotionEffect, …) — register them here too, else the load throws on the first
            // unknown component key. The editor path registers them on its own registry above.
            nativeRegistry.RegisterGameComponents();
            var nativeSerializer = new SceneSerializer(nativeRegistry);
            var nativeAssetTextures = new FileAssetTextureLoader(_graphicsDevice, _content.RootDirectory);
            // The shipped reader needs the prefab expander too: a bundled scene may carry linked
            // prefab instances (PF-C), and a reader without the expander fails the whole load on
            // the first `prefab` entry (the editor path reuses the overlay's expander above).
            var nativePrefabExpander = new PrefabExpander(
                nativeSerializer,
                new PrefabFileSource(_content.RootDirectory, _projectContext).Resolve,
                loadTexture: key => _content.Load<Texture2D>(key),
                fileTextureLoader: nativeAssetTextures.Load);
            nativeSceneReader = new SceneReaderSystem(_world, nativeSerializer, _content,
                fileTextureLoader: nativeAssetTextures.Load, prefabExpander: nativePrefabExpander);
        }
        // Source-first when the editor's project is resolved (UX-D pre-mortem #5): a Restart-after-Save
        // re-publishes LoadLevelRequest through this probe, and the source tree — not the stale bundle —
        // must win. A shipped build (null context) keeps the bundled TitleContainer path byte-identical.
        var nativeSceneProbe = NativeLevelLoader.CreateProbe(_world, _content.RootDirectory, _projectContext);

        var p = _updatePipeline;
        p.AddGroup("input", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("cursor", cursorInputSystem);
            if (replaySystem != null) g.Add("replay", replaySystem);
            g.Add("mapping", inputMappingSystem);
        }, inputKind, _parallelRunner);
        p.AddGroup("levelLoad", EditTimeBehavior.RunNormally, g =>
        {
            if (_importMode)
            {
                // Import machinery (dev/export op): re-parse the legacy LDtk level so the
                // importer can capture + serialize it. No native probe — force the legacy parse even if
                // a .mdscene already exists (re-import). The importer takes the parsed world afterwards.
                g.Add("requests", new LevelLoadRequestSystem(_world, _content, tryLoadNativeScene: null, enableLegacyLdtkFallback: true));
                g.Add("ldtkTiles", new LDtkTileParserSystem(_world, _content));
                g.Add("ldtkEntities", new LDtkEntityParserSystem(_world));
                g.Add("entitySpawn", entitySpawnSystem);
            }
            else
            {
                // Native-only game boot (PS5): the game loads bundled .mdscene levels through the native
                // reader; the legacy LDtk loader is import-only (composed only above), so a
                // LoadLevelRequest with no native scene fails loud — no silent legacy attempt. This is
                // the single content-driven load path that closes the parser-asymmetry.
                g.Add("requests", new LevelLoadRequestSystem(_world, _content, nativeSceneProbe, enableLegacyLdtkFallback: false));
                // The native reader for a shipped game (no editor). When the editor is composed, its own
                // editor.sceneReader (below) is the single reader — do not double-subscribe here.
                if (_editor == null) g.Add("nativeSceneReader", nativeSceneReader);
            }
        });
        if (_editor != null)
        {
            // Native-scene loading (LoadSceneRequest) — with the level-load group, message-driven.
            p.Add("editor.sceneReader", _editor.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Dialog, EditTimeBehavior.RunNormally);
            // Woven immediately after the dialog so the dialog wins when both could open (UX2-D).
            p.Add("editor.contextMenu", _editor.Menu, EditTimeBehavior.RunNormally);
            // The editor shortcut owner (UX3-E) — right after the modal input-owners so dialog/menu
            // suppression wins; the context gate makes it inert while Playing.
            p.Add("editor.shortcuts", _editor.Shortcuts, EditTimeBehavior.RunNormally);
            // UX3-F: the modal transform owner — enters via editor.shortcuts (G/S/R), owns the pointer +
            // keyboard while active. Right after the shortcuts, before the tools + the draw selection, so
            // its pointer-consume reaches them.
            p.Add("editor.modal", _editor.Modal, EditTimeBehavior.RunNormally);
            // Boundary bake — reacts to a BoundaryComponent being added/changed (the tool's commit,
            // a scene load, a vertex edit) and generates the segment colliders. RunNormally: a
            // shipped game loading a native scene with a boundary must bake it too (§S2).
            p.Add("editor.boundaryBake", _editor.BoundaryBake, EditTimeBehavior.RunNormally);
        }
        // Game logic + physics + dialogue — FROZEN in Edit (runs only in Play; the group's single
        // Freeze gate skips all children, exactly like the old opaque composite). The collision
        // chain must stay sequential (movement → velocity → detect → resolve → commit); individual
        // systems keep their internal _parallelRunner for entity-level parallelism.
        p.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("movement", new MovementSystem(_world, _parallelRunner));
            g.Add("orbs", new OrbSystem(_world));
            g.Add("stopMotion", new StopMotionEffectSystem(_world));
            g.Add("velocity", new TransformVelocitySystem(_world, _parallelRunner));
            g.Add("collisionDetect",
                new TransformCollisionDetectionSystem<CollisionMessage>(_world, GameCollisionHelper.Create));
            g.Add("collisionResolve", new TransformPhysicalCollisionResolutionSystem(_world));
            g.Add("transformCommit", new TransformCommitSystem(_world, _parallelRunner));
            g.Add("textUpdate", new TextUpdateSystem(_world)); // Logic only
            g.Add("npcInteraction", new NPCInteractionSystem(_world));
            g.Add("zoneDialogue", new ZoneDialogueTriggerSystem(_world));
            g.Add("dialogue", new DialogueSystem(
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
                nameof(EntityType.Interface)));
            // ... other game logic systems
        });
        if (_editor != null)
        {
            // Delete/undo/redo, then the gizmo — BEFORE HierarchySystem so a transform edit
            // propagates to world space the same frame. Both Edit-guarded internally. The collider
            // proxy sync follows the gizmo so the proxies re-derive from this frame's write-back.
            p.Add("editor.commands", _editor.EditorCommands, EditTimeBehavior.RunNormally);
            p.Add("editor.gizmo", _editor.Gizmo, EditTimeBehavior.RunNormally);
            p.Add("editor.proxySync", _editor.ProxySync, EditTimeBehavior.RunNormally);
        }
        // HierarchySystem stays LIVE in Edit (RunNormally) — editor edits propagate this frame.
        p.Add("hierarchy", hierarchySystem, EditTimeBehavior.RunNormally);
        // Camera-follow FREEZES in Edit (the editor owns the camera there).
        p.Add("cameraFollow", cameraFollowSystem, EditTimeBehavior.Freeze);
        if (_editor != null)
        {
            // Toolbar mesh prep + clicks (hidden in Play), the systems panel (after the toolbar,
            // whose mesh prep bakes its checkbox meshes), then edit-time camera navigation —
            // BEFORE CursorPositionSystem so the camera mutation this frame is what the cursor's
            // world position derives from (no one-frame lag).
            p.AddGroup("editor.toolbar", EditTimeBehavior.RunNormally, g =>
            {
                g.Add("meshPrep", _editor.ToolbarMeshPrep);
                g.Add("clicks", _editor.ToolbarClicks);
                g.Add("tooltip", _editor.Tooltip);
                g.Add("viewportTabs", _editor.ViewportTabs); // PF-B: the viewport tab strip
            });
            p.Add("editor.systemsPanel", _editor.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.inspector", _editor.Inspector, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.CameraNav, EditTimeBehavior.RunNormally);
        }
        p.Add("cursorPosition", cursorLateUpdateSystem, EditTimeBehavior.RunNormally);
        if (_editor?.Palette != null)
            // The asset palette + placement — AFTER CursorPositionSystem so the ghost preview
            // follows THIS frame's cursor world position (no one-frame lag). Edit-guarded.
            p.Add("editor.palette", _editor.Palette, EditTimeBehavior.RunNormally);
        if (_editor != null)
            // The freeform boundary tool — also after CursorPositionSystem so a lay click reads
            // this frame's cursor world position. Edit-guarded (its own Update checks the mode).
            p.Add("editor.boundary", _editor.BoundaryTool, EditTimeBehavior.RunNormally);
        p.Add("cursorDrawPrep", new CursorDrawPrepSystem(_world), EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            // The Blender-style shell sync: viewport inset + native chrome layout + cursor swap
            // track the run mode. AFTER CursorDrawPrepSystem so hiding the game cursor sprite in
            // Edit takes effect the same frame (the prep would otherwise re-stamp its texture).
            p.Add("editor.shell", _editor.Shell, EditTimeBehavior.RunNormally);
            p.Add("editor.statusBar", _editor.StatusBar, EditTimeBehavior.RunNormally); // UX3-F: window status bar
        }
        if (_editor?.EditorOpDriver != null)
            // The headless editor-op driver — LAST, after the cursor late update, so its injected
            // cursor is the final word the gizmo/toolbar read. Plan-gated: only present when an
            // editor_op_plan.json exists (zero cost in a normal run).
            p.Add("editor.opDriver", _editor.EditorOpDriver, EditTimeBehavior.RunNormally);

        return p.Build();
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = SettingsManager.Instance.Settings.PixelPerfectRendering;

        // One render pass per view: the world (Main) through the camera, plus screen-space
        // UI and HUD. Compose more instances for minimaps / splitscreen / CCTV / portals.
        var mainPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera);
        var uiPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI]);
        var hudPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]);

        // Final system composites the render targets onto the back buffer. With the editor
        // composed, the native-resolution chrome layer goes on top — it resolves to null (and is
        // skipped) outside Edit, so Play compositing is identical to a screen without the editor.
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
        // The DrawComponent prep chain, as a group with named children (panel-visible).
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("culling", new CullingSystem(_world, _camera));
            g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering));
            g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering));
            g.Add("meshPrep", new MeshPrepSystem(_world));
            g.Add("colliderDebug", new ColliderDebugSystem(_world));
            g.Add("spriteDebug", new SpriteDebugSystem(_world));
            // ... other systems preparing DrawElements (UI, particles, etc.)
        });
        if (_editor != null)
            // Selection runs at the END of the prep phase so it reads the FINAL post-YSort
            // DrawComponent.LayerDepth computed THIS frame. The cursor's click edge (set in the
            // update phase) survives into the draw call, so picking is in-frame. Edit-guarded.
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
            // The native-resolution chrome pass (Editor target). Edit-only internally: outside
            // Edit it renders nothing and its final-draw layer resolves to null (skipped).
            p.Add("editor.renderChrome", _editor.ChromeRender, EditTimeBehavior.RunNormally);
        p.Add("finalDraw", finalDrawToScreenSystem, EditTimeBehavior.RunNormally);
        p.Add("screenshots", screenshotSystem, EditTimeBehavior.RunNormally);

        return p.Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}
