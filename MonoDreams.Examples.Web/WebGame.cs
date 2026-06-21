using System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
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
    /// replay) — those are gated out of Examples.Core's web build. It boots straight into
    /// <see cref="LoadLevelExampleGameScreen"/> on the LDtk <c>Level_0</c> so the browser
    /// proof renders the platformer and takes keyboard input.
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
            _graphics.PreferredBackBufferWidth = _settings.WindowWidth;
            _graphics.PreferredBackBufferHeight = _settings.WindowHeight;
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

            _screenController.RegisterScreen(ScreenName.Game,
                () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));

            // Boot straight into the LDtk platformer proof level.
            Services.AddService(new RequestedLevelComponent("Level_0"));
            _screenController.LoadScreen(ScreenName.Game);

            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            _screenController.Update(gameTime);
            base.Update(gameTime);
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
