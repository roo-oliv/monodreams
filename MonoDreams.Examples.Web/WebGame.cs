using System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Screens;
using MonoDreams.Examples.Settings;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;

namespace MonoDreams.Examples.Web
{
    /// <summary>
    /// The BlazorGL host for the MonoDreams reference app. Mirrors the desktop
    /// <c>Game1</c> screen-assembly path but drops every desktop-only concern (ImGui
    /// inspector, OS window position/resize events, headless 1x1 mode, file-based input
    /// replay) — those are gated out of Examples.Core's web build. It registers the same
    /// three screens as desktop and boots into the <see cref="LevelSelectionScreen"/> menu,
    /// so the browser flow matches <c>Game1</c> (desktop has no replay plan ⇒ menu; web has
    /// no replay plan at all ⇒ menu).
    ///
    /// GraphicsProfile is Reach (BlazorGL/WebGL); the engine's MONODREAMS_WEB gate already
    /// routes the desktop HiDef choice to Reach. The Game loop is driven by the Blazor page
    /// (requestAnimationFrame -> Tick), same as the Phase 0 spike.
    /// </summary>
    public class WebGame : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private ViewportManager _viewportManager;
        private Camera _camera;
        private DefaultParallelRunner _runner;
        private ScreenController _screenController;
        private readonly GameSettings _settings;

        public WebGame()
        {
            _settings = SettingsManager.Instance.Settings;

            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.GraphicsProfile = GraphicsProfile.Reach;
            // Do NOT force a back-buffer size: on BlazorGL the back buffer follows the canvas drawing
            // buffer (sized by the host page to the aspect-fit display size, 1:1). Forcing a size here
            // desyncs the render from the canvas (content offset/clipped). The per-frame viewport sync
            // keeps ScreenWidth matched to the actual back buffer.
            _graphics.ApplyChanges();

            _viewportManager = new ViewportManager(this, _settings.VirtualWidth, _settings.VirtualHeight);
            _camera = new Camera(_settings.VirtualWidth, _settings.VirtualHeight);
        }

        protected override void Initialize()
        {
            var debugDir = PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
            Logger.Initialize(debugDir);
            Logger.Info("WebGame initializing (BlazorGL).");

            _viewportManager.ScreenWidth = GraphicsDevice.Viewport.Width;
            _viewportManager.ScreenHeight = GraphicsDevice.Viewport.Height;
            _camera.RecalculateTransformationMatrices();

            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.BlendState = BlendState.AlphaBlend;

            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _runner = new DefaultParallelRunner(1);
            _screenController = new ScreenController(this, _runner, _viewportManager, _camera, _spriteBatch, Content);
            _camera.Zoom = _settings.CameraZoom;
            _camera.Position = Vector2.Zero;

            // Same screen set as desktop Game1 — the platform is the only difference, the
            // game flow is identical (develop once, build everywhere).
            // NOTE: the desktop `--editor` / MONODREAMS_EDITOR=1 run flag (editor overlay on the
            // plain game screen + boot-in-Edit) is not wired here yet — a browser has no launch
            // args/env vars, so the web equivalent (a query-string switch read through JS interop)
            // is a documented follow-up. The LevelEditor screen below is fully editor-capable.
            _screenController.RegisterScreen(ScreenName.LevelSelection,
                () => new LevelSelectionScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
            _screenController.RegisterScreen(ScreenName.Game,
                () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
            _screenController.RegisterScreen(ScreenName.InfiniteRunner,
                () => new InfiniteRunnerScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
            _screenController.RegisterScreen(ScreenName.LevelEditor,
                () => new LevelEditorScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));

            // Web has no file-based replay plan (the desktop skip-to-level mechanism), so it
            // always takes desktop's default branch: open the level-selection menu.
            _screenController.LoadScreen(ScreenName.LevelSelection);

            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            // The browser canvas (and thus the GL backbuffer) can change size at any time and web
            // has no Window.ClientSizeChanged event, so poll each frame and re-sync the viewport
            // (the desktop heads do this from the resize event).
            SyncViewportToBackbuffer();
            _screenController.Update(gameTime);
            base.Update(gameTime);
        }

        private void SyncViewportToBackbuffer()
        {
            var w = GraphicsDevice.Viewport.Width;
            var h = GraphicsDevice.Viewport.Height;
            if (w == _viewportManager.ScreenWidth && h == _viewportManager.ScreenHeight) return;

            _viewportManager.ScreenWidth = w;
            _viewportManager.ScreenHeight = h;
            _camera.RecalculateTransformationMatrices();
        }

        protected override void Draw(GameTime gameTime)
        {
            _screenController.Draw(gameTime);
            base.Draw(gameTime);
        }

        protected override void Dispose(bool disposing)
        {
            _screenController?.Dispose();
            Logger.Shutdown();
            _runner?.Dispose();
            _spriteBatch?.Dispose();
            _graphics?.Dispose();
            base.Dispose(disposing);
        }
    }
}
