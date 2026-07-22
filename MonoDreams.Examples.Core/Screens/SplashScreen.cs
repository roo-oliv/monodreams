using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Examples.Settings;
using MonoDreams.Input;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// Boot splash: the MonoDreams logo (waves + waning moon ASCII art, see <c>Icon/ascii/</c>) on a
/// black backdrop, held for at least <see cref="MinimumSeconds"/> before handing off to the real
/// boot screen. Composed by both heads (desktop <c>Game1</c>, web <c>WebGame</c>) on a normal
/// interactive boot only — headless runs, replay plans (test timing contracts), the editor run
/// flag, and the export op all skip it and load their target screen directly.
///
/// The screen uses the reference pipeline, not a parallel renderer: two sprite entities
/// (backdrop quad + logo) on the UI target (screen-space, no camera), prepped by
/// <see cref="SpritePrepSystem"/> and drawn by the sole <see cref="MasterRenderSystem"/>.
/// The backdrop is an opaque black quad covering the whole virtual viewport, so the splash is
/// black regardless of <see cref="FinalDrawSystem.ClearColor"/> (a static the game may theme).
/// </summary>
public class SplashScreen : IGameScreen
{
    /// <summary>Minimum time the logo stays on screen before the next screen loads.</summary>
    public const float MinimumSeconds = 1.5f;

    /// <summary>Content key of the logo texture (bundled from <c>Icon/ascii/monodreams-logo.png</c>).</summary>
    public const string LogoContentKey = "Logo/monodreams-logo";

    private readonly GraphicsDevice _graphicsDevice;
    private readonly ViewportManager _viewportManager;
    private readonly World _world;
    private readonly RenderTarget2D _uiTarget;
    private readonly string _nextScreen;

    private ScreenController? _screenController;
    private Texture2D? _backdropPixel;
    private float _shownAt = -1f;
    private bool _transitioned;

    public SplashScreen(GraphicsDevice graphicsDevice, ViewportManager viewportManager,
        SpriteBatch spriteBatch, string nextScreen)
    {
        _graphicsDevice = graphicsDevice;
        _viewportManager = viewportManager;
        _nextScreen = nextScreen;
        _world = new World();
        _uiTarget = new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight);

        UpdateSystem = new ActionSystem<GameState>(Tick);

        // Screenshot capture (parity with the other screens, gated on a replay plan asking for
        // screenshots): a shorter interval than the game screens' 2s, so a verification run can
        // actually catch the 1.5s splash frame.
        var debugDir = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_DEBUG_DIR")
            ?? PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        var replayPlan = InputReplayPlan.TryLoad(debugDir);
        var screenshotSystem = new ScreenshotCaptureSystem(graphicsDevice, captureIntervalSeconds: 0.5f, debugDir)
        {
            IsEnabled = replayPlan?.Screenshots ?? false
        };

        var pixelPerfect = SettingsManager.Instance.Settings.PixelPerfectRendering;
        DrawSystem = new SequentialSystem<GameState>(
            new SpritePrepSystem(_world, graphicsDevice, pixelPerfect),
            new MasterRenderSystem(spriteBatch, graphicsDevice, _world, RenderTargetID.UI, _uiTarget),
            new FinalDrawSystem(spriteBatch, graphicsDevice, viewportManager,
                new List<RenderLayer> { RenderLayer.UI(_uiTarget) }),
            screenshotSystem);
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;

        var vw = _viewportManager.VirtualWidth;
        var vh = _viewportManager.VirtualHeight;

        // Opaque black backdrop over the whole virtual viewport (layer 0 = back).
        _backdropPixel = new Texture2D(_graphicsDevice, 1, 1);
        _backdropPixel.SetData(new[] { Color.White });
        var backdrop = _world.CreateEntity();
        backdrop.Set(new EntityInfoComponent("SplashBackdrop"));
        backdrop.Set(new TransformComponent(Vector2.Zero));
        backdrop.Set(new SpriteInfoComponent
        {
            SpriteSheet = _backdropPixel,
            Source = new Rectangle(0, 0, 1, 1),
            Size = new Vector2(vw, vh),
            Color = Color.Black,
            Target = RenderTargetID.UI,
            LayerDepth = 0f,
        });
        backdrop.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.UI });
        backdrop.Set<VisibleComponent>();

        // The logo, centered, scaled to ~42% of the virtual height (it is portrait: 1024x1177) —
        // a modest brand mark, not a poster.
        var logo = content.Load<Texture2D>(LogoContentKey);
        var height = vh * 0.425f;
        var width = height * logo.Width / logo.Height;
        var logoEntity = _world.CreateEntity();
        logoEntity.Set(new EntityInfoComponent("SplashLogo"));
        logoEntity.Set(new TransformComponent(new Vector2((vw - width) / 2f, (vh - height) / 2f)));
        logoEntity.Set(new SpriteInfoComponent
        {
            SpriteSheet = logo,
            AssetKey = LogoContentKey,
            Source = logo.Bounds,
            Size = new Vector2(width, height),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = 0.5f,
        });
        logoEntity.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.UI });
        logoEntity.Set<VisibleComponent>();

        Logger.Info($"Splash: showing MonoDreams logo for at least {MinimumSeconds:0.0}s before '{_nextScreen}'.");
    }

    private void Tick(GameState state)
    {
        // Anchor on the first update's TotalTime (screen construction/load time is excluded, so
        // the logo is actually VISIBLE for the minimum hold, not merely alive).
        if (_shownAt < 0f)
        {
            _shownAt = state.TotalTime;
            Logger.Info($"Splash: hold anchored at TotalTime={state.TotalTime:0.000}s.");
            return;
        }
        if (_transitioned || state.TotalTime - _shownAt < MinimumSeconds) return;
        _transitioned = true;
        Logger.Info($"Splash: minimum hold elapsed; loading screen '{_nextScreen}'.");
        _screenController?.LoadScreen(_nextScreen);
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        _uiTarget.Dispose();
        _world.Dispose();
        _backdropPixel?.Dispose();
        GC.SuppressFinalize(this);
    }
}
