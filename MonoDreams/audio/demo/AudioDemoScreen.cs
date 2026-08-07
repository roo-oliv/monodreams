using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Audio;
using MonoDreams.Component.Draw;
using MonoDreams.Demos;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Message;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Audio;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demo.Audio;

/// Audio module demo — the three playback idioms, mixing simultaneously:
///
/// - **One-shot** (`PlaySoundRequest`): a click ping fired and forgotten (C key / button).
/// - **Looping ambience** (`AudioSourceComponent` with `Loop = true`): a wind loop toggled
///   by flipping the source's desired `State` (W key / checkbox).
/// - **Interruptible source** (non-loop `AudioSourceComponent`): a ~10s jukebox riff started
///   with `State = Playing` and cut mid-play with `State = Stopped` (J / K keys / buttons).
///
/// Each source owns its own <c>SoundEffectInstance</c>, so all three sound at once.
///
/// A short frame-scripted boot sequence (<see cref="AudioDemoDirectorSystem"/>) demonstrates
/// each case once and the screen logs a <c>Logger.Info</c> line on every playback start/stop —
/// the observable that <c>HeadlessAudioDemoTests</c> asserts, since audio is inaudible in a
/// headless run.
public class AudioDemoScreen : IGameScreen
{
    /// <summary>The scene id this demo is bound to (TD/UX-C): its editor Save writes
    /// <c>audio-demo.mdscene</c> and the Scenes panel lists it as a scene.</summary>
    public const string BoundSceneId = "audio-demo";

    // Content keys — built from MonoDreams.Demos/Content/Sounds/*.wav (procedurally
    // generated; see generate_demo_sounds.py next to them).
    private const string ClickSoundKey = "Sounds/click";
    private const string WindSoundKey = "Sounds/wind";
    private const string JukeboxSoundKey = "Sounds/jukebox";

    private const float ClickVolume = 0.6f;
    private const float WindVolume = 0.4f;
    private const float JukeboxVolume = 0.5f;

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;

    /// The screen owns the player (see the audio module's postInstallNotes): created here,
    /// handed to the single AudioSystem, disposed with the screen AFTER the pipelines.
    private readonly ContentAudioPlayer _audioPlayer;

    private ScreenController? _screenController;
    private Entity _windSource;
    private Entity _jukeboxSource;
    private Entity _windToggle;
    private Entity _windStatusText;
    private Entity _jukeboxStatusText;
    private bool _windOn = true;
    private bool _jukeboxPlaying;

    // The universal editor overlay (null when editorEnabled is false) and the retained pipeline
    // registries the editor's systems panel binds to (see DemoEditor).
    private readonly bool _editorEnabled;
    private readonly EditorSession _session;
    private readonly EditorProjectContext? _projectContext;
    private readonly DrawLayerMap _layers = DemoEditor.CreateLayers();
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private DemoEditor? _editor;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public AudioDemoScreen(GraphicsDevice graphicsDevice, ContentManager content,
        MonoDreams.Component.Camera camera, ViewportManager viewportManager, SpriteBatch spriteBatch,
        bool editorEnabled = false, EditorSession session = null, EditorProjectContext projectContext = null)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _editorEnabled = editorEnabled;
        _session = session;
        _projectContext = projectContext;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI,   new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD,  new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
        };
        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");
        _audioPlayer = new ContentAudioPlayer(content);
        // WARMED here — the screen's loading moment, next to the font load. A SoundEffect is a disk
        // read plus a PCM decode on first request and Play runs mid-frame, so an unwarmed demo
        // hitches once on the first click, once on the wind, once on the jukebox; warming pays that
        // cost where a hitch is invisible. A key that fails to warm is logged and skipped (never
        // fatal) and stays on the lazy path in Play.
        _audioPlayer.Preload(new[] { ClickSoundKey, WindSoundKey, JukeboxSoundKey });

        camera.Position = Vector2.Zero;

        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.Overlay.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(AudioDemoScreen), _updatePipeline, _drawPipeline);
        }
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnButtonClicked);

        MonoDreams.Cursor.Cursor.CreateMesh(_world,
            ShapeBuilder.Arrow(26f, Color.Black, Color.White).Generate(), RenderTargetID.HUD);

        BuildScene(content);

        if (_editor != null)
        {
            // TD split seam: the code-content rebuild re-creates the audio source entities and the
            // HUD (all disposed by the sweep). Disposing the source entities cuts their live
            // instances (the lifecycle premise); the rebuild restarts the wind if it was on.
            _editor.Overlay.Transport.RebuildCodeContent = () => BuildScene(_content);
            _editor.BindScene(screenController, _world, _content.RootDirectory, DemoScreens.Audio, BoundSceneId);
        }
    }

    private void BuildScene(ContentManager content)
    {
        CreateAudioSources();
        BuildHud(content);
        if (_windOn) Logger.Info("Audio demo: wind loop started.");
    }

    // ─── audio source entities ────────────────────────────────────────────────

    /// The two lifecycle sources: the looping wind ambience (on by default) and the
    /// stopped jukebox riff. One-shots need no entity — they are published as messages.
    private void CreateAudioSources()
    {
        var wind = _world.CreateEntity();
        var windSource = new AudioSourceComponent(WindSoundKey, loop: true, volume: WindVolume)
        {
            State = _windOn ? AudioPlaybackState.Playing : AudioPlaybackState.Stopped,
        };
        wind.Set(windSource);
        _windSource = wind;

        var jukebox = _world.CreateEntity();
        var jukeboxSource = new AudioSourceComponent(JukeboxSoundKey, volume: JukeboxVolume)
        {
            State = AudioPlaybackState.Stopped,
        };
        jukebox.Set(jukeboxSource);
        _jukeboxSource = jukebox;
        _jukeboxPlaying = false;
    }

    // ─── public bridges for the keyboard / director systems ──────────────────

    public void GoBackToLauncher() => _screenController?.LoadScreen(DemoScreens.Launcher);

    /// One-shot: publish and forget — AudioSystem starts one instance and releases it
    /// when it finishes. No entity involved.
    public void PlayClick()
    {
        _world.Publish(new PlaySoundRequest(ClickSoundKey, ClickVolume));
        Logger.Info("Audio demo: one-shot click fired.");
    }

    public void ToggleWind()
    {
        if (!_windToggle.IsAlive || !_windToggle.Has<ToggleSwitchComponent>()) return;
        var sw = _windToggle.Get<ToggleSwitchComponent>();
        sw.On = !sw.On;
        _windToggle.Set(sw);
        SetWind(sw.On);
    }

    private void SetWind(bool on)
    {
        _windOn = on;
        if (_windSource.IsAlive && _windSource.Has<AudioSourceComponent>())
            _windSource.Get<AudioSourceComponent>().State = on ? AudioPlaybackState.Playing : AudioPlaybackState.Stopped;
        Logger.Info(on ? "Audio demo: wind loop started." : "Audio demo: wind loop stopped.");
        UpdateStatusTexts();
    }

    /// Starts the jukebox riff from the beginning. Idempotent while playing, so the boot
    /// sequence and a user's J press never double-start (or double-log) it.
    public void StartJukebox()
    {
        if (!_jukeboxSource.IsAlive || !_jukeboxSource.Has<AudioSourceComponent>()) return;
        var source = _jukeboxSource.Get<AudioSourceComponent>();
        if (source.State == AudioPlaybackState.Playing) return;
        source.State = AudioPlaybackState.Playing;
        _jukeboxPlaying = true;
        Logger.Info("Audio demo: jukebox started.");
        UpdateStatusTexts();
    }

    /// The interruption showcase: cuts the live riff mid-play by writing the desired
    /// state — AudioSystem stops and releases the instance on its next reconcile.
    public void CutJukebox()
    {
        if (!_jukeboxSource.IsAlive || !_jukeboxSource.Has<AudioSourceComponent>()) return;
        var source = _jukeboxSource.Get<AudioSourceComponent>();
        if (source.State != AudioPlaybackState.Playing) return;
        source.State = AudioPlaybackState.Stopped;
        _jukeboxPlaying = false;
        Logger.Info("Audio demo: jukebox cut mid-play.");
        UpdateStatusTexts();
    }

    /// Detects the one state flip the demo doesn't make itself: AudioSystem flipping a
    /// finished non-loop source back to Stopped (the played-to-completion case). Called
    /// every frame by <see cref="AudioDemoDirectorSystem"/>.
    public void SyncJukeboxStatus()
    {
        if (!_jukeboxSource.IsAlive || !_jukeboxSource.Has<AudioSourceComponent>()) return;
        var playing = _jukeboxSource.Get<AudioSourceComponent>().State == AudioPlaybackState.Playing;
        if (playing == _jukeboxPlaying) return;
        _jukeboxPlaying = playing;
        if (!playing)
        {
            Logger.Info("Audio demo: jukebox finished (played to completion).");
            UpdateStatusTexts();
        }
    }

    // ─── button click routing ────────────────────────────────────────────────

    private void OnButtonClicked(in DemoButtonClicked msg)
    {
        switch (msg.Id)
        {
            case DemoHeader.BackId: GoBackToLauncher(); break;
            case DemoHeader.ExitId: _screenController?.Game.Exit(); break;
            case "audio.click":         PlayClick(); break;
            case "toggle.wind":         ToggleWind(); break;
            case "audio.jukebox-start": StartJukebox(); break;
            case "audio.jukebox-cut":   CutJukebox(); break;
        }
    }

    // ─── HUD ────────────────────────────────────────────────────────────────

    private void BuildHud(ContentManager content)
    {
        DemoHeader.Build(
            _world, _viewportManager, _font,
            title: "audio",
            descriptionLines: new[]
            {
                "One-shot SFX, a looping ambience and an interruptible jukebox.",
                "A short boot sequence plays each case once - then it's yours.",
            });

        BuildSidebar();
        BuildStatusPanel();
    }

    private void BuildSidebar()
    {
        var capStyle = new KeyCapStyle
        {
            CapPixels = 42,
            CapLabelScale = 0.22f,
        };
        var rowStyle = new KeyRowStyle
        {
            LabelColor = DemoPalette.TextLight,
            HoverColor = DemoPalette.TextHover,
            ActiveColor = DemoPalette.TextSelected,
            LabelScale = 0.18f,
            Gap = 10f,
            BackgroundColor = DemoPalette.DarkBgSecondary,
            HoverBackgroundColor = DemoPalette.DarkBgSecondary,
            ActiveBackgroundColor = DemoPalette.DarkBgSecondary,
            BackgroundPaddingX = 10f,
            BackgroundPaddingY = 6f,
        };

        (Entity Container, Entity Outline, Vector2 Size) Row(string id, string key, string label) =>
            _world.CreateKeyRow(id, key, label, _font, capStyle, rowStyle, layerDepth: 0.96f);

        var click = Row("audio.click", "C", "play click (one-shot)");

        var wind = _world.CreateCheckboxRow(
            id: "toggle.wind",
            rowLabel: "wind loop",
            font: _font,
            initiallyOn: _windOn,
            boxSize: 42f,
            row: rowStyle,
            layerDepth: 0.96f);
        _windToggle = wind.Outline;

        var jukeboxStart = Row("audio.jukebox-start", "J", "start jukebox");
        var jukeboxCut = Row("audio.jukebox-cut", "K", "cut jukebox");

        const float rowGap = 6f;
        const float groupGap = 16f;

        Entity Spacer() => _world.CreateEntity();

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopLeft, RenderTargetID.HUD)
            .Direction(LayoutDirection.Vertical)
            .Gap(rowGap)
            .Padding(20, 12, 12, 12)
            .AlignCross(CrossAxisAlignment.Start)
            .AddSlot(slot => slot.Attach(click.Container).MeasureWith(_ => click.Size))
            .AddSlot(slot => slot.Attach(Spacer()).MeasureWith(_ => new Vector2(0, groupGap - rowGap)))
            .AddSlot(slot => slot.Attach(wind.Container).MeasureWith(_ => wind.Size))
            .AddSlot(slot => slot.Attach(Spacer()).MeasureWith(_ => new Vector2(0, groupGap - rowGap)))
            .AddSlot(slot => slot.Attach(jukeboxStart.Container).MeasureWith(_ => jukeboxStart.Size))
            .AddSlot(slot => slot.Attach(jukeboxCut.Container).MeasureWith(_ => jukeboxCut.Size))
            .Build();
    }

    /// Centered live status readout for the two lifecycle sources (the one-shot has no
    /// lifecycle to report). Text content is mutated in place on every state change.
    ///
    /// Placed with explicit transforms rather than a third AutoLayout root: multiple root
    /// layouts vertically stack under one screen root (see the DemoHeader remark), so a
    /// Center-anchored root built after the header + sidebar roots would land near the
    /// bottom edge, not the centre. The camera is fixed at the origin, so world (0,0) is
    /// the screen centre; each line is centered once at build time — the status words swap
    /// between equal-length strings ("playing"/"stopped") in a monospace font, so the
    /// centering stays true as the content mutates.
    private void BuildStatusPanel()
    {
        _windStatusText = DemoUI.CreateText(_world, WindStatusLabel(),
            _font, DemoPalette.TextLight, scale: 0.24f, layerDepth: 0.5f);
        _jukeboxStatusText = DemoUI.CreateText(_world, JukeboxStatusLabel(),
            _font, DemoPalette.TextLight, scale: 0.24f, layerDepth: 0.5f);
        var hint = DemoUI.CreateText(_world, "each source is its own instance - they all mix",
            _font, DemoPalette.TextHover, scale: 0.18f, layerDepth: 0.5f);

        CenterAt(_windStatusText, y: -52);
        CenterAt(_jukeboxStatusText, y: -12);
        CenterAt(hint, y: 40);
    }

    /// Horizontally centres a text entity at the given world-space y (text draws from its
    /// transform's top-left).
    private static void CenterAt(Entity textEntity, float y)
    {
        var size = DemoUI.MeasureText(textEntity);
        ref var transform = ref textEntity.Get<TransformComponent>();
        transform.Position = new Vector2(-size.X / 2f, y);
    }

    private string WindStatusLabel() => _windOn ? "wind loop: playing" : "wind loop: stopped";

    private string JukeboxStatusLabel() => _jukeboxPlaying ? "jukebox: playing" : "jukebox: stopped";

    private void UpdateStatusTexts()
    {
        if (_windStatusText.IsAlive && _windStatusText.Has<DynamicTextComponent>())
            _windStatusText.Get<DynamicTextComponent>().TextContent = WindStatusLabel();
        if (_jukeboxStatusText.IsAlive && _jukeboxStatusText.Has<DynamicTextComponent>())
            _jukeboxStatusText.Get<DynamicTextComponent>().TextContent = JukeboxStatusLabel();
    }

    // ─── pipeline ────────────────────────────────────────────────────────────

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var cursorInputSystem = new CursorInputSystem(_world, _viewportManager);

        // The editor overlay (see DemoEditor): built over THIS screen's world/camera/layers.
        _editor = DemoEditor.TryCreate(_editorEnabled, _world, _camera, _layers, _content,
            _graphicsDevice, _spriteBatch, _viewportManager, () => _screenController?.Game,
            session: _session, projectContext: _projectContext, sceneId: BoundSceneId);
        // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
        if (_editor?.Overlay.HasEditorOpPlan == true) cursorInputSystem.SkipHardwareRead = true;

        // ---- Weave the update pipeline through the registrar. With the editor off every gate
        // is a pass-through in Play and the order matches a plain demo screen exactly. ----
        var p = _updatePipeline;
        p.Add("input", cursorInputSystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.keys", _editor.Keys, EditTimeBehavior.RunNormally);
            p.Add("editor.sceneReader", _editor.Overlay.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Overlay.Dialog, EditTimeBehavior.RunNormally);
            p.Add("editor.contextMenu", _editor.Overlay.Menu, EditTimeBehavior.RunNormally);
            // WS: the Autotile Rules workspace — after the modal input-owners (it stands down while a
            // dialog/menu owns the pointer) and before the shortcuts, whose gate ORs its IsOpen.
            p.Add("editor.rules", _editor.Overlay.RulesEditor, EditTimeBehavior.RunNormally);
            // The editor shortcut owner (UX3-E) — after the modal input-owners; inert while Playing.
            p.Add("editor.shortcuts", _editor.Overlay.Shortcuts, EditTimeBehavior.RunNormally);
            p.Add("editor.modal", _editor.Overlay.Modal, EditTimeBehavior.RunNormally); // UX3-F: G/S/R modal transforms
        }
        p.AddGroup("layout", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("intrinsicSizing", new IntrinsicSizingSystem(_world));
            g.Add("autoLayout", new AutoLayoutSystem(_world, _viewportManager));
        });
        // Demo UI interaction FREEZES in Edit: a click belongs to the editor, never to a
        // toggle / back / exit (which would tear the screen down mid-editing).
        p.AddGroup("ui.interaction", EditTimeBehavior.Freeze, g =>
        {
            g.Add("buttons", new DemoButtonInteractionSystem(_world));
            g.Add("toggles", new ToggleSwitchSystem(_world));
        });
        // Demo logic (keyboard + the boot sequence) freezes in Edit like any game logic.
        p.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("demoInput", new AudioDemoInputSystem(this));
            g.Add("director", new AudioDemoDirectorSystem(this));
        });
        // The module's single system. Audio is game logic → Freeze in Edit (the reference
        // policy; see the audio premises). Freeze stops RECONCILIATION, not already-live
        // instances — a playing loop keeps sounding in Edit (documented v1 limitation).
        p.Add("audio", new AudioSystem(_world, _audioPlayer), EditTimeBehavior.Freeze);
        if (_editor != null)
        {
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
                g.Add("viewportTabs", _editor.Overlay.ViewportTabs); // PF-B: the viewport tab strip
                g.Add("workspaceTabs", _editor.Overlay.WorkspaceTabs); // WS: the top-bar workspace tab strip
            });
            p.Add("editor.systemsPanel", _editor.Overlay.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.Overlay.CameraNav, EditTimeBehavior.RunNormally);
            // TD/PF-F universal palette (composes with a resolved project; empty assetRoots is legal).
            if (_editor.Overlay.Palette != null)
                p.Add("editor.palette", _editor.Overlay.Palette, EditTimeBehavior.RunNormally);
        }
        p.Add("cursorPosition", new CursorPositionSystem(_world, _camera, _viewportManager),
            EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.shell", _editor.Overlay.Shell, EditTimeBehavior.RunNormally);
            p.Add("editor.statusBar", _editor.Overlay.StatusBar, EditTimeBehavior.RunNormally); // UX3-F: window status bar
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
        // With the editor composed, the sprite prep chain (cull → sprite prep → Y-sort) is added
        // so a native scene loaded while editing actually previews; the demo DrawLayerMap has no
        // Y-sorted layer, so YSortSystem passes depths through — documented graceful degradation.
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            if (_editorEnabled) g.Add("culling", new CullingSystem(_world, _camera));
            g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false));
            if (_editorEnabled) g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering: false));
            g.Add("meshPrep", new MeshPrepSystem(_world));
            g.Add("buttonMeshPrep", new ButtonMeshPrepSystem(_world));
        });
        if (_editor != null)
        {
            p.Add("editor.selection", _editor.Overlay.Selection, EditTimeBehavior.RunNormally);
            p.Add("editor.overlayPrep", _editor.Overlay.OverlayPrep, EditTimeBehavior.RunNormally);
        }
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
        // Pipelines first (AudioSystem.Dispose stops every live instance through the player),
        // then the player itself — the screen owns it (see the module postInstallNotes).
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        _audioPlayer.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ─── input ────────────────────────────────────────────────────────────────

/// Edge-triggered keyboard shortcuts for the audio demo.
public class AudioDemoInputSystem : ISystem<GameState>
{
    private readonly AudioDemoScreen _screen;
    private KeyboardState _previous;
    public bool IsEnabled { get; set; } = true;

    public AudioDemoInputSystem(AudioDemoScreen screen)
    {
        _screen = screen;
        _previous = Keyboard.GetState();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        var current = Keyboard.GetState();
        bool Pressed(Keys k) => current.IsKeyDown(k) && !_previous.IsKeyDown(k);

        if (Pressed(Keys.C)) _screen.PlayClick();
        if (Pressed(Keys.W)) _screen.ToggleWind();
        if (Pressed(Keys.J)) _screen.StartJukebox();
        if (Pressed(Keys.K)) _screen.CutJukebox();
        if (Pressed(Keys.Escape)) _screen.GoBackToLauncher();

        _previous = current;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

// ─── boot sequence + status sync ──────────────────────────────────────────

/// One-time boot showcase + per-frame jukebox status sync. Frame-counted rather than
/// gametime-based so the whole sequence lands inside a headless run's frame budget
/// regardless of the max-speed clock: click at frame 30, jukebox start at 90, jukebox
/// cut at 300 — 0.5s / 1.5s / 5s at interactive 60 fps, all within the default
/// 600-frame headless run. The cut is genuinely mid-play in both worlds: the riff is
/// ~10s long and <c>SoundEffectInstance</c> playback advances on the wall clock.
/// The screen's start/cut bridges are idempotent, so the sequence composes safely
/// with early user input.
public class AudioDemoDirectorSystem(AudioDemoScreen screen) : ISystem<GameState>
{
    private const int ClickFrame = 30;
    private const int JukeboxStartFrame = 90;
    private const int JukeboxCutFrame = 300;

    private int _frame;
    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        if (_frame == ClickFrame) screen.PlayClick();
        if (_frame == JukeboxStartFrame) screen.StartJukebox();
        if (_frame == JukeboxCutFrame) screen.CutJukebox();
        if (_frame <= JukeboxCutFrame) _frame++;

        screen.SyncJukeboxStatus();
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
