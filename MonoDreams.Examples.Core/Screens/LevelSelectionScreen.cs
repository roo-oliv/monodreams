using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.Settings;
using MonoDreams.Examples.System;
using MonoDreams.Extension;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Platform;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;
using MonoDreams.Examples.System.UI;
using MonoDreams.Renderer;
using MonoDreams.UI;
using MonoDreams.Draw;
using MonoDreams.Examples.Draw;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// Screen for selecting which level to load. Like every Examples screen since Wave 8a, its
/// pipelines are built through the <see cref="EditorPipelineRegistrar"/> (each entry named +
/// run-state-gated) and, when <c>editorEnabled</c> is true (the <c>--editor</c> /
/// <c>MONODREAMS_EDITOR=1</c> run flag), the <see cref="EditorOverlay"/> is composed over this
/// screen's own world — the editor is screen-agnostic; a menu is as editable a scene as a level.
/// With the flag off nothing editor-related is constructed and the pipeline is behaviourally
/// identical to the pre-editor screen (RunMode never leaves Play; the gates are pass-throughs).
///
/// <para><b>Pointer model (issue #115).</b> The menu picks through the <c>ui</c> module: its
/// buttons carry <see cref="FocusableComponent"/>, <see cref="UIFocusSystem"/> is the single owner
/// of the pointer pick / focus / activation, and this screen's <c>ButtonInteractionSystem</c> only
/// reacts to it. That is what lets the buttons carry a <see cref="TooltipComponent"/> (the label
/// rides the same pick) and what keeps the click, the hover colour and the tooltip from ever
/// disagreeing — see the ui premise "One click, one owner".</para>
///
/// <para><b>Menu-specific edit policies:</b> <c>ui.interaction</c> (the pick + the button click →
/// screen-transition system) is <c>Freeze</c> — while the transport is Paused a click belongs to
/// the editor (selection / gizmo / chrome), so menu buttons must not fire mid-editing; press the
/// toolbar's Play transport button to use the menu, or re-enable the entry live from the systems
/// panel. <c>ui.tooltip</c> freezes with it (it despawns its label through
/// <c>ISuspendableSystem</c>, so nothing is stranded on the never-frozen HUD pass).
/// <c>layout</c> stays <c>RunNormally</c>:
/// the auto-layout solver is the menu's content placement (the analogue of the game screen's level
/// parsers, which also run in Edit) — freezing it would boot an unlaid-out menu under
/// <c>--editor</c>. A menu button is layout slot CONTENT (its root is a <c>ChildOf</c> child of the
/// slot the builder creates), and <c>AutoLayoutSystem</c> writes only SLOT transforms — never the
/// content's — so a gizmo/modal move of a button edits its LOCAL offset under its slot and STICKS
/// (undoable): the layout owns where the slot is anchored, and the manual offset composes on top. So
/// menu buttons ARE editable (TB-B), not the old "layout-owned, not gizmo-editable" degradation.</para>
/// </summary>
public class LevelSelectionScreen : IGameScreen
{
    // ── Lofi palette, shared by the menu's widgets and by its tooltip style ───────────────────────
    private static readonly Color DarkBrown = new(60, 50, 45);     // main text
    private static readonly Color Terracotta = new(200, 120, 80);  // hover / accent
    private static readonly Color MutedBrown = new(150, 140, 130); // disabled
    private static readonly Color Parchment = new(238, 230, 216);  // tooltip panel fill (opaque!)

    /// <summary>The scene id this screen is bound to (UX-C): its editor Save writes
    /// <c>level_selection.mdscene</c>, and on boot its optional-scene-load brings that scene up under
    /// the code-built menu UI. Referenced by the host's <see cref="ScreenInfo"/> so the binding is
    /// declared once.</summary>
    public const string BoundSceneId = "level_selection";

    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;
    private readonly DrawLayerMap _layers;

    // Wave 8a: the universal editor overlay (null when editorEnabled is false) and the retained
    // pipeline registries the systems panel binds to.
    private readonly bool _editorEnabled;
    private readonly EditorProjectContext? _projectContext;
    private readonly EditorSession _session;
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private EditorOverlay _editor;

    public LevelSelectionScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch,
        bool editorEnabled = false, EditorProjectContext? projectContext = null, EditorSession session = null)
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
        _session = session;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) }
        };

        // Load font early so it's available for systems
        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        camera.Position = Vector2.Zero;

        _layers = DrawLayerMap.FromEnum<DrawLayer>();
        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(LevelSelectionScreen), _updatePipeline, _drawPipeline);
        }
    }

    private ScreenController? _screenController;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;

        // Subscribe to screen transition requests
        _world.Subscribe<ScreenTransitionRequest>(OnScreenTransitionRequest);

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1"),
            [CursorType.Pointer] = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 2"),
            [CursorType.Hand] = content.Load<Texture2D>("Mouse sprites/Catpaw Mouse icon"),
        };

        // Create cursor entity
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        // Create level selection UI
        CreateLevelSelectionUI();

        if (_editor != null)
        {
            // TB-A: this screen hosts the level_selection scene tab — name the active tab so the strip +
            // Save target track it (the session's boot tab, or the cross-screen target this activation lands).
            _editor.SetSceneId(BoundSceneId);
            // TD split seam: the code-content rebuild is the menu's UI builder (the sweep disposed the UI
            // entities; the cursor survives), and the scene-content reload is the optional bound-scene load.
            // Restart runs BOTH (the UX-D full rebuild — source-first, so a backup-reload restores the last
            // SAVE, not the last build); a Game-tab exit / scene switch runs ONLY RebuildCodeContent between
            // the sweep and the snapshot restore, so closing the Game tab keeps the menu buttons alive
            // instead of a blank screen (the report-2 fix) without double-loading the scene from disk.
            _editor.Transport.RebuildCodeContent = CreateLevelSelectionUI;
            _editor.Transport.ReloadSceneContent = () =>
                NativeLevelLoader.TryPublishSceneLoad(_world, _content.RootDirectory, BoundSceneId, _projectContext);
            // Optional scene load (UX-C): bring level_selection.mdscene up under the code-built menu
            // UI if it exists (source-first, then bundled; absent → silently skip). The code UI stays.
            // TB-A: SKIP it when a cross-screen tab activation restores this tab's in-memory snapshot
            // instead — the restore runs the reader OVER the code-built UI (no sweep, no double content).
            if (!_editor.RestorePendingActivation(screenController.State))
                NativeLevelLoader.TryPublishSceneLoad(_world, _content.RootDirectory, BoundSceneId, _projectContext);
            // The Scenes panel + the tab-open/activate switch (Examples hand-off).
            _editor.BindSceneCatalog(ScreenName.LevelSelection,
                () => screenController.RegisteredScreens,
                entry => EditorSceneSwitch.Switch(screenController, entry));
        }
    }

    [Subscribe]
    private void OnScreenTransitionRequest(in ScreenTransitionRequest request)
    {
        // Store the requested level in the world so the next screen can access it
        if (request.LevelIdentifier != null)
        {
            _screenController?.Game.Services.AddService(new RequestedLevelComponent(request.LevelIdentifier));
        }

        // Transition to the requested screen
        _screenController?.LoadScreen(request.ScreenName);
    }

    private void CreateLevelSelectionUI()
    {
        // Create button style from the lofi palette (the class-level fields — the tooltip style,
        // built where the pipeline is assembled, shares them).
        var buttonStyle = ButtonStyle.WithColors(DarkBrown, Terracotta, MutedBrown);

        // Create entities first. Play buttons only — the editor is entered exclusively through the
        // --editor / MONODREAMS_EDITOR=1 run configuration (the transport model), never via a menu
        // button. Level ids resolve to NATIVE .mdscene levels now (PS5: native-first is native-only;
        // the legacy LDtk loader is import-only). "Level 1" → the migrated native Blender_Level;
        // the runner is a screen, not a level file. The LDtk Level_0 is not migrated yet (its ~21k
        // per-tile entities need a native tile-layer batching primitive — a PS6 item), so it is not
        // offered here: booting it native-only would fail loud.
        var titleEntity = CreateTextEntity("Select Level", _font, DarkBrown, scale: 0.3f, _layers.GetDepth(DrawLayer.Title));
        var play1 = CreateButtonEntity("Level 1", _font, 0, "Blender_Level", true, buttonStyle,
            tooltip: "Native scene: Blender_Level.mdscene");
        var play2 = CreateButtonEntity("Runner", _font, 1, null, true, buttonStyle, ScreenName.InfiniteRunner,
            tooltip: "A screen, not a level file");

        // "Start here": the generic attention primitive (HighlightComponent + HighlightSystem)
        // pointing at the default choice. One component, no bespoke glow art and no per-frame
        // bookkeeping — the system derives the outline from whatever the button DRAWS, re-derives
        // its depth every frame, and disposes it with the button. Removing the component (or the
        // button) removes the hint; nothing else has to know it existed.
        play1.container.Set(new HighlightComponent
        {
            Color = Terracotta,
            Padding = 6f,
            Thickness = 2f,
            PulseSpeed = 0.6f,
            PulseMinIntensity = 0.4f,
        });

        // Create UI using auto layout with slots
        var layout = new AutoLayoutBuilder(_world, _viewportManager);

        layout.CreateRoot(ScreenAnchor.Center)
            .Name("Root")
            .Direction(LayoutDirection.Vertical)
            .Gap(40)
            .AlignMain(MainAxisAlignment.Center)
            .AlignCross(CrossAxisAlignment.Center)
            .AddSlot(slot => slot
                .Attach(titleEntity)
                .MeasureWith(MeasureText))
            .AddContainer(column => column
                .Name("ButtonColumn")
                .Direction(LayoutDirection.Vertical)
                .Gap(50)
                .AlignCross(CrossAxisAlignment.Center)
                .AddSlot(slot => slot.Attach(play1.container).MeasureWith(_ => play1.size))
                .AddSlot(slot => slot.Attach(play2.container).MeasureWith(_ => play2.size))
            )
            .Build();
    }

    private Entity CreateTextEntity(string text, BitmapFont font, Color color, float scale, float layerDepth)
    {
        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));
        entity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = layerDepth,
            TextContent = text,
            Font = font,
            Color = color,
            Scale = scale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        entity.Set<VisibleComponent>();
        return entity;
    }

    private (Entity container, Vector2 size) CreateButtonEntity(
        string text,
        BitmapFont font,
        int levelIndex,
        string levelName,
        bool isClickable,
        ButtonStyle style,
        string targetScreen = null,
        string tooltip = null)
    {
        // Measure text to determine button size
        var textSize = font.MeasureString(text) * style.TextScale;
        var buttonSize = new Vector2(
            textSize.Width + style.Padding * 2,
            textSize.Height + style.Padding * 2);

        // TB-B button hierarchy: ONE root entity carries the transform (the move/select/gizmo handle),
        // the pickable + interaction surface (SimpleButtonComponent, whose outline mesh
        // ButtonMeshPrepSystem draws), and the LevelSelector behavior. The label is a ChildOf CHILD, so
        // select / move / G / S operate on the root and the label follows through the ordinary
        // hierarchy — no more separate container + shared-transform hack. This is the single button
        // shape across Examples + Demos (the no-duplicate-ways tenet).
        var buttonEntity = _world.CreateEntity();
        buttonEntity.Set(new TransformComponent(Vector2.Zero));

        // Label: its own transform (offset by padding), parented to the button root.
        var buttonTextEntity = _world.CreateEntity();
        buttonTextEntity.Set(new TransformComponent(new Vector2(style.Padding, style.Padding)));
        buttonTextEntity.SetParent(buttonEntity);
        buttonTextEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = _layers.GetDepth(DrawLayer.ButtonText),
            TextContent = text,
            Font = font,
            Color = isClickable ? style.DefaultColor : style.DisabledColor,
            Scale = style.TextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        buttonTextEntity.Set<VisibleComponent>();

        buttonEntity.Set(new SimpleButtonComponent
        {
            Size = buttonSize,
            LineThickness = style.BorderThickness,
            Color = style.BorderColor,
            TextEntity = buttonTextEntity,
            Target = RenderTargetID.Main
        });
        // The button is PICKABLE: one component makes it visible to UIFocusSystem, which is the
        // single owner of "what is the pointer over?" — hover, keyboard focus, press and the
        // activation edge ButtonInteractionSystem acts on, plus the PointerPickComponent every
        // hover consumer rides. Target=Main because the menu draws in world space (the pick then
        // compares the cursor's WorldPosition), and the size is the button's own box.
        buttonEntity.Set(new FocusableComponent
        {
            TabIndex = levelIndex,
            Group = 0,
            Disabled = !isClickable,
            Size = buttonSize,
            Target = RenderTargetID.Main,
        });
        // "Hover to learn": pure data on the pickable entity. TooltipSystem owns the floating label
        // end-to-end (dwell, placement, edge flip, teardown) — the screen writes one string.
        if (!string.IsNullOrEmpty(tooltip)) buttonEntity.Set(new TooltipComponent { Text = tooltip });
        buttonEntity.Set(new LevelSelector
        {
            LevelIndex = levelIndex,
            LevelName = levelName,
            TargetScreen = targetScreen,
            IsClickable = isClickable,
            IsHovered = false,
            DefaultColor = style.DefaultColor,
            HoveredColor = style.HoveredColor,
            DisabledColor = style.DisabledColor
        });
        buttonEntity.Set<VisibleComponent>();

        return (buttonEntity, buttonSize);
    }

    private static Vector2 MeasureText(Entity entity)
    {
        if (!entity.Has<DynamicTextComponent>()) return Vector2.Zero;

        ref var text = ref entity.Get<DynamicTextComponent>();
        var measuredSize = text.Font.MeasureString(text.TextContent);
        return new Vector2(measuredSize.Width * text.Scale, measuredSize.Height * text.Scale);
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var cursorInputSystem = new CursorInputSystem(_world, _viewportManager);

        // The editor overlay (Wave 8a): built over THIS screen's world/camera/layers — the menu is
        // a scene like any other. The chrome uses the same PPMondwest font as the game screen's
        // chrome (the content pipeline caches it) so the shell reads identically everywhere.
        if (_editorEnabled)
        {
            var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
                ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
            var chromeFont = _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");
            _editor = new EditorOverlay(
                _world, _camera, _layers, _content, chromeFont, _graphicsDevice, _spriteBatch,
                _viewportManager,
                // Delete / frame / undo / redo are the EditorShortcuts chord table (UX3-E), read off the
                // raw keyboard — no game-supplied bindings needed here (this screen has no tool keys).
                new EditorInputBindings(),
                debugDir,
                requestExit: _game.Exit,
                setOsCursorVisible: visible => _game.IsMouseVisible = visible,
                sceneId: BoundSceneId, // explicit per-screen id (UX-C) — Save targets level_selection.mdscene
                projectContext: _projectContext,
                session: _session);
            // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
            if (_editor.HasEditorOpPlan) cursorInputSystem.SkipHardwareRead = true;
        }

        // Hierarchy system must run AFTER any systems that modify transforms
        // but BEFORE any systems read world transforms (rendering, etc.)
        var hierarchySystem = new HierarchySystem(_world);

        // Cursor position must update after layout/UI to use current camera state
        var cursorLateUpdateSystem = new CursorPositionSystem(_world, _camera, _viewportManager);

        // Scripted mouse replay (issue #90): present only when debug/pointer_replay.json is — a normal
        // run builds nothing here. A scripted pointer owns BOTH cursor halves (the hardware read and
        // the screen→virtual→world derivation), and runs immediately after the cursor-input stage so
        // ui.interaction reads the injected click the same frame a real one would be read. The camera
        // derives world coordinates from the authored virtual ones; the viewport manager maps those
        // same coordinates forward into the backbuffer pixels ScreenPosition is contractually in.
        var pointerReplaySystem = PointerReplaySystem.TryLoad(
            PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
                ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug"),
            _world, _camera, _viewportManager, requestExit: _game.Exit);
        if (pointerReplaySystem != null)
        {
            cursorInputSystem.SkipHardwareRead = true;
            cursorLateUpdateSystem.SkipDerivation = true;
            if (_editor?.HasEditorOpPlan == true)
                Logger.Warning("Both a pointer-replay plan and an editor-op plan are present — two "
                               + "channels will fight over the same cursor. Keep one per run.");
        }

        // ---- Weave the update pipeline through the registrar. With the editor off every entry
        // is RunNormally/pass-through and the order matches the pre-editor screen exactly. ----
        var p = _updatePipeline;
        p.Add("input", cursorInputSystem, EditTimeBehavior.RunNormally);
        if (pointerReplaySystem != null)
            p.Add("pointerReplay", pointerReplaySystem, EditTimeBehavior.RunNormally);
        // The game's ONE keyboard mapping, composed on the menu too (it used to be editor-only):
        // UIFocusSystem below navigates the menu from these actions, so the menu is keyboard-usable
        // — WASD/arrow keys to move between buttons, Tab / Shift-Tab to cycle, Space to activate —
        // and the editor gets the key surface its modal suppression needs from the same instance.
        var keys = new InputMappingSystem(_world);
        if (_editor != null)
        {
            // Modal capture (keyboard half): the editor/game keyboard (incl. Escape-to-exit) stands
            // down while a Save/Load dialog owns the keys; the mouse half is the dialog consuming the
            // cursor edges.
            keys.ShouldSuppressInput = () => _editor.Dialog.IsOpen || _editor.Menu.IsOpen || _editor.Modal.IsActive
                || _editor.InspectorOwnsKeyboard || _editor.RulesEditor.IsOpen;
        }
        p.Add("input.keys", keys, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            // Native-scene loading (LoadSceneRequest) — the toolbar's Load button needs a handler.
            p.Add("editor.sceneReader", _editor.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Dialog, EditTimeBehavior.RunNormally);
            // Woven immediately after the dialog so the dialog wins when both could open (UX2-D).
            p.Add("editor.contextMenu", _editor.Menu, EditTimeBehavior.RunNormally);
            // WS: the Autotile Rules workspace — after the modal input-owners (it stands down while a
            // dialog/menu owns the pointer) and before the shortcuts, whose gate ORs its IsOpen.
            p.Add("editor.rules", _editor.RulesEditor, EditTimeBehavior.RunNormally);
            // The editor shortcut owner (UX3-E) — after the modal input-owners; inert while Playing.
            p.Add("editor.shortcuts", _editor.Shortcuts, EditTimeBehavior.RunNormally);
            p.Add("editor.modal", _editor.Modal, EditTimeBehavior.RunNormally); // UX3-F: G/S/R modal transforms
        }
        // The auto-layout solver is the menu's content placement (the level-parser analogue):
        // RunNormally, or booting straight into Edit would show an unlaid-out menu. Trade-off:
        // layout-managed transforms are solver-owned, so they are not gizmo-editable. Layout
        // systems must run before UI systems to position elements.
        p.AddGroup("layout", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("intrinsicSizing", new IntrinsicSizingSystem(_world)); // measure via callbacks
            g.Add("autoLayout", new AutoLayoutSystem(_world, _viewportManager)); // calc + apply
            // Debug visualization (toggle with LayoutDebugSystem.Enabled).
            g.Add("debug", new LayoutDebugSystem(_world, _font, _camera));
        });
        // Menu button interaction FREEZES while Paused (Edit): a click there belongs to the editor
        // (selection / gizmo / chrome), never to a screen transition. The toolbar's Play transport
        // button or the systems panel re-arms it. ONE gate on the whole group, so the pick and the
        // action it feeds can never be half-frozen.
        p.AddGroup("ui.interaction", EditTimeBehavior.Freeze, g =>
        {
            // The single owner of "what is the pointer over?" — it resolves the topmost focusable
            // under the cursor once, drives hover/press/activation from it, and publishes it as
            // PointerPickComponent for the hover consumers (the tooltip below). Nav actions come
            // from the game's own key mapping ("input.keys" above), so the menu is keyboard-usable;
            // the ACTION stays game-side, in ButtonInteractionSystem's UIFocusActivated handler.
            g.Add("focus", new UIFocusSystem(_world,
                InputState.Up, InputState.Down, InputState.Left, InputState.Right,
                InputState.MenuNext, InputState.MenuPrevious, InputState.Jump));
            g.Add("buttons", new ButtonInteractionSystem(_world));
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
        p.Add("hierarchy", hierarchySystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.AddGroup("editor.toolbar", EditTimeBehavior.RunNormally, g =>
            {
                g.Add("meshPrep", _editor.ToolbarMeshPrep);
                g.Add("clicks", _editor.ToolbarClicks);
                g.Add("tooltip", _editor.Tooltip);
                g.Add("viewportTabs", _editor.ViewportTabs); // PF-B: the viewport tab strip
                g.Add("workspaceTabs", _editor.WorkspaceTabs); // WS: the top-bar workspace tab strip
            });
            p.Add("editor.systemsPanel", _editor.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.inspector", _editor.Inspector, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.CameraNav, EditTimeBehavior.RunNormally);
            // PF-F universal palette: weave the asset/prefab palette when the overlay composed one (it
            // builds a default catalog + bands from the resolved project + this screen's layer map even
            // though the menu supplies none) — so a menu gets the Assets + Prefabs tabs too.
            if (_editor.Palette != null)
                p.Add("editor.palette", _editor.Palette, EditTimeBehavior.RunNormally);
            // The tile-grid paint brush, right after the palette. This screen supplies no paint
            // defaults, so "New Indexed Layer" refuses loud — the brush is simply inert here.
            p.Add("editor.tilePaint", _editor.TilePaint, EditTimeBehavior.RunNormally);
        }
        p.Add("cursorPosition", cursorLateUpdateSystem, EditTimeBehavior.RunNormally);
        // The floating label rides the pick published by "ui.interaction/focus" AND the pointer's
        // FRESH virtual position, so it sits after both. Freeze is safe even though it OWNS entities:
        // TooltipSystem implements ISuspendableSystem, so the gate despawns the live label on the
        // Play → Pause edge instead of stranding it on the (never-frozen) HUD pass.
        p.Add("ui.tooltip", new TooltipSystem(_world, _viewportManager, _font, new TooltipStyle
        {
            Delay = 0.35f,
            TextScale = 0.14f,
            TextColor = Parchment,
            Fill = DarkBrown,      // opaque: the mesh path composites premultiplied alpha
            Outline = Terracotta,
        }), EditTimeBehavior.Freeze);
        p.Add("cursorDrawPrep", new CursorDrawPrepSystem(_world), EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.shell", _editor.Shell, EditTimeBehavior.RunNormally);
            p.Add("editor.statusBar", _editor.StatusBar, EditTimeBehavior.RunNormally); // UX3-F: window status bar
        }
        if (_editor?.EditorOpDriver != null)
            p.Add("editor.opDriver", _editor.EditorOpDriver, EditTimeBehavior.RunNormally);

        return p.Build();
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = SettingsManager.Instance.Settings.PixelPerfectRendering;

        var mainPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera);
        var uiPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI], _viewportManager.LayoutCamera);
        var hudPass = new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD], _viewportManager.LayoutCamera);

        var renderLayers = new List<RenderLayer>
        {
            RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
            RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
            RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
        };
        if (_editor != null)
            renderLayers.Add(_editor.ChromeLayer);
        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, renderLayers);

        // Debug screenshot capture (parity with the other screens): off unless a replay plan asks
        // for screenshots, so a normal run is unaffected.
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        var screenshotSystem = new ScreenshotCaptureSystem(_graphicsDevice, captureIntervalSeconds: 2.0f, debugDir)
        {
            IsEnabled = replayPlan?.Screenshots ?? false
        };

        // ---- Weave the draw pipeline through the registrar (retained for the systems panel). ----
        var p = _drawPipeline;
        // The menu's own content is text + button meshes, so it needs only TextPrepSystem. With
        // the editor composed, the sprite prep chain (cull → sprite prep → Y-sort) is added so a
        // native scene loaded/pasted while editing actually previews (self-sufficient overlay);
        // the menu's DrawLayerMap has no Y-sorted layer, so YSortSystem passes depths through and
        // selection picks on the final (source-derived) LayerDepth — documented degradation.
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            if (_editorEnabled)
            {
                g.Add("culling", new CullingSystem(_world, _camera));
                g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering));
                g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            }
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering));
            // Local-space meshes (the tooltip panel TooltipSystem spawns is one) get their world
            // matrix here — in the DRAW stage, so a panel created late in the update pipeline is
            // positioned on the very frame it appears rather than a frame later at the origin.
            g.Add("meshPrep", new MeshPrepSystem(_world));
            // …and the button outlines, whose vertices are already baked in world space, take the
            // opposite contract: they must overwrite MeshPrepSystem's matrix with the identity, so
            // they run immediately AFTER it (ui premise "ButtonMeshPrepSystem bakes world coords and
            // must run AFTER MeshPrepSystem whenever both are in the pipeline"). The menu keeps
            // rebuilding them in Edit — it must go on rendering while it is edited.
            g.Add("buttonMeshPrep", new ButtonMeshPrepSystem(_world));
            // LAST in draw prep, by contract: the highlight outline is derived from what its target
            // is ABOUT to draw (here the button mesh the line above just baked) and its depth is
            // re-read from that same component every frame, so it can never sink under the thing it
            // points at.
            g.Add("highlight", new HighlightSystem(_world));
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

    public enum DrawLayer
    {
        Cursor,      // 1.0 - front
        ButtonText,  // middle
        Title,       // 0.0 - back
    }
}
