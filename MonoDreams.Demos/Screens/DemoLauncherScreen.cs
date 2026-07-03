using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Demos.UI;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demos.Screens;

/// Menu screen listing every available module demo. Click a button to switch
/// into the demo screen.
public class DemoLauncherScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;

    // The universal editor overlay (null when editorEnabled is false) and the retained pipeline
    // registries the editor's systems panel binds to (see DemoEditor).
    private readonly bool _editorEnabled;
    private readonly DrawLayerMap _layers = DemoEditor.CreateLayers();
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private DemoEditor? _editor;

    private ScreenController? _screenController;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public DemoLauncherScreen(GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, SpriteBatch spriteBatch, bool editorEnabled = false)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _editorEnabled = editorEnabled;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
        };

        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");
        camera.Position = Vector2.Zero;

        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.Overlay.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(DemoLauncherScreen), _updatePipeline, _drawPipeline);
        }
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnDemoButtonClicked);

        MonoDreams.Cursor.Cursor.CreateMesh(_world,
            ShapeBuilder.Arrow(26f, Color.Black, Color.White).Generate(), RenderTargetID.HUD);

        BuildUI();
    }

    private void OnDemoButtonClicked(in DemoButtonClicked msg)
    {
        if (_screenController == null) return;
        switch (msg.Id)
        {
            case "camera":
                _screenController.LoadScreen(DemoScreens.Camera);
                break;
            case "physics":
                _screenController.LoadScreen(DemoScreens.Physics);
                break;
            case "dialogue":
                _screenController.LoadScreen(DemoScreens.Dialogue);
                break;
            case "ui":
                _screenController.LoadScreen(DemoScreens.Ui);
                break;
            case DemoHeader.ExitId:
                _screenController.Game.Exit();
                break;
        }
    }

    private void BuildUI()
    {
        // Menu buttons: greyscale ramp (grey outline + dark label, fill-only state). The
        // label depth must clear the button fill mesh (baked at 0.95 by ButtonMeshPrepSystem).
        const float buttonTextDepth = 0.96f;
        var style = new ButtonStyle
        {
            BorderThickness = 2f,
            Padding = 18f,
            TextScale = 0.22f,
        };

        var title = DemoUI.CreateText(_world, "MonoDreams Module Demos", _font,
            DemoPalette.TextLight, scale: 0.40f, layerDepth: 0.5f);
        var subtitle = DemoUI.CreateText(_world, "Pick a module below to open its working demonstration.", _font,
            DemoPalette.TextHover, scale: 0.20f, layerDepth: 0.5f);

        var cameraBtn = DemoUI.CreateButton(_world, "camera", "camera", _font, style, buttonTextDepth);
        var physicsBtn = DemoUI.CreateButton(_world, "physics", "physics", _font, style, buttonTextDepth);
        var dialogueBtn = DemoUI.CreateButton(_world, "dialogue", "dialogue", _font, style, buttonTextDepth);
        var uiBtn = DemoUI.CreateButton(_world, "ui", "ui", _font, style, buttonTextDepth);
        // A disabled entry shows the muted-grey disabled style (and doesn't dispatch a click).
        var soonBtn = DemoUI.CreateButton(_world, "soon", "more demos soon", _font, style, buttonTextDepth,
            disabled: true);

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.Center)
            .Direction(LayoutDirection.Vertical)
            .Gap(20)
            .AlignCross(CrossAxisAlignment.Center)
            .AddSlot(slot => slot.Attach(title).MeasureWith(DemoUI.MeasureText))
            .AddSlot(slot => slot.Attach(subtitle).MeasureWith(DemoUI.MeasureText))
            .AddSlot(slot => slot.Attach(_world.CreateEntity()).MeasureWith(_ => new Vector2(0, 16)))
            .AddSlot(slot => slot.Attach(cameraBtn.container).MeasureWith(_ => cameraBtn.size))
            .AddSlot(slot => slot.Attach(physicsBtn.container).MeasureWith(_ => physicsBtn.size))
            .AddSlot(slot => slot.Attach(dialogueBtn.container).MeasureWith(_ => dialogueBtn.size))
            .AddSlot(slot => slot.Attach(uiBtn.container).MeasureWith(_ => uiBtn.size))
            .AddSlot(slot => slot.Attach(soonBtn.container).MeasureWith(_ => soonBtn.size))
            .Build();

        // Single exit chrome button (top-right) styled as a Q-key chip so it
        // matches the demo header's exit row.
        var capStyle = new KeyCapStyle
        {
            CapPixels = 32,
            CapLabelScale = 0.13f,
        };
        var rowStyle = new KeyRowStyle
        {
            LabelColor = DemoPalette.TextLight,
            HoverColor = DemoPalette.TextHover,
            ActiveColor = DemoPalette.TextSelected,
            LabelScale = 0.18f,
            Gap = 8f,
            BackgroundColor = DemoPalette.DarkBgSecondary,
            HoverBackgroundColor = DemoPalette.DarkBgSecondary,
            ActiveBackgroundColor = DemoPalette.DarkBgSecondary,
            BackgroundPaddingX = 10f,
            BackgroundPaddingY = 6f,
        };
        var exitRow = _world.CreateKeyRow(
            id: DemoHeader.ExitId, keyLabel: "Q", rowLabel: "exit",
            font: _font, cap: capStyle, row: rowStyle,
            layerDepth: 0.96f, target: RenderTargetID.HUD);

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopRight, RenderTargetID.HUD)
            .Direction(LayoutDirection.Horizontal)
            .Padding(4 /* top */, 8 /* right */, 12 /* bottom */, 8 /* left */)
            .AddSlot(slot => slot.Attach(exitRow.Container).MeasureWith(_ => exitRow.Size))
            .Build();
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var cursorInputSystem = new CursorInputSystem(_world);

        // The editor overlay (see DemoEditor): built over THIS screen's world/camera/layers —
        // the launcher menu is a scene like any other.
        _editor = DemoEditor.TryCreate(_editorEnabled, _world, _camera, _layers, _content,
            _graphicsDevice, _spriteBatch, _viewportManager, () => _screenController?.Game);
        // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
        if (_editor?.Overlay.HasEditorOpPlan == true) cursorInputSystem.SkipHardwareRead = true;

        // ---- Weave the update pipeline through the registrar. With the editor off every entry
        // is RunNormally/pass-through and the order matches the pre-editor screen exactly. ----
        var p = _updatePipeline;
        p.Add("input", cursorInputSystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            // The Demos host runs no keyboard-action mapping of its own; the editor brings its
            // default key surface (Delete, Z/Y, Home) — composed only under the flag.
            p.Add("editor.keys", _editor.Keys, EditTimeBehavior.RunNormally);
            // Native-scene loading (LoadSceneRequest) — the toolbar's Load button needs a handler.
            p.Add("editor.sceneReader", _editor.Overlay.SceneReader, EditTimeBehavior.RunNormally);
        }
        // The auto-layout solver is the menu's content placement: RunNormally, or booting
        // straight into Edit would show an unlaid-out menu.
        p.AddGroup("layout", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("intrinsicSizing", new IntrinsicSizingSystem(_world));
            g.Add("autoLayout", new AutoLayoutSystem(_world, _viewportManager));
        });
        // Menu button interaction FREEZES in Edit: a click there belongs to the editor, never to
        // a screen transition (which would tear this screen down mid-editing). The toolbar's
        // Play transport button re-arms it.
        p.Add("ui.interaction", new DemoButtonInteractionSystem(_world), EditTimeBehavior.Freeze);
        if (_editor != null)
        {
            // Delete/undo/redo, then the gizmo — BEFORE HierarchySystem so a transform edit
            // propagates to world space the same frame; the proxy sync re-derives from the same
            // frame's write-back. All Edit-guarded internally.
            p.Add("editor.commands", _editor.Overlay.EditorCommands, EditTimeBehavior.RunNormally);
            p.Add("editor.gizmo", _editor.Overlay.Gizmo, EditTimeBehavior.RunNormally);
            p.Add("editor.proxySync", _editor.Overlay.ProxySync, EditTimeBehavior.RunNormally);
        }
        p.Add("hierarchy", new HierarchySystem(_world), EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.AddGroup("editor.toolbar", EditTimeBehavior.RunNormally, g =>
            {
                g.Add("meshPrep", _editor.Overlay.ToolbarMeshPrep);
                g.Add("clicks", _editor.Overlay.ToolbarClicks);
            });
            p.Add("editor.systemsPanel", _editor.Overlay.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.Overlay.CameraNav, EditTimeBehavior.RunNormally);
        }
        p.Add("cursorPosition", new CursorPositionSystem(_world, _camera, _viewportManager),
            EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.shell", _editor.Overlay.Shell, EditTimeBehavior.RunNormally);
            if (_editor.Overlay.EditorOpDriver != null)
                p.Add("editor.opDriver", _editor.Overlay.EditorOpDriver, EditTimeBehavior.RunNormally);
        }

        return p.Build();
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var renderLayers = new List<RenderLayer>
        {
            RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
            RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
            RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
        };
        if (_editor != null)
            renderLayers.Add(_editor.Overlay.ChromeLayer);

        // ---- Weave the draw pipeline through the registrar (retained for the systems panel). ----
        var p = _drawPipeline;
        // The launcher's own content is text + button/cursor meshes. With the editor composed,
        // the sprite prep chain (cull → sprite prep → Y-sort) is added so a native scene loaded
        // while editing actually previews; the demo DrawLayerMap has no Y-sorted layer, so
        // YSortSystem passes depths through — documented graceful degradation.
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            if (_editorEnabled) g.Add("culling", new CullingSystem(_world, _camera));
            g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false));
            if (_editorEnabled) g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering: false));
            g.Add("meshPrep", new MeshPrepSystem(_world));
            // ButtonMeshPrepSystem must run AFTER MeshPrepSystem: button outlines are
            // baked in world coords and clear WorldMatrix to identity.
            g.Add("buttonMeshPrep", new ButtonMeshPrepSystem(_world));
        });
        if (_editor != null)
            p.Add("editor.selection", _editor.Overlay.Selection, EditTimeBehavior.RunNormally);
        p.Add("renderMain", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera), EditTimeBehavior.RunNormally);
        p.Add("renderUI", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI]), EditTimeBehavior.RunNormally);
        p.Add("renderHUD", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]), EditTimeBehavior.RunNormally);
        if (_editor != null)
            p.Add("editor.renderChrome", _editor.Overlay.ChromeRender, EditTimeBehavior.RunNormally);
        p.Add("finalDraw", new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, renderLayers),
            EditTimeBehavior.RunNormally);

        return p.Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }
}
