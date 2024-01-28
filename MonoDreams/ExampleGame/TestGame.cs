using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;

namespace MonoDreams;

public class TestGame : Game
{
    #region Fields

    private readonly GraphicsDeviceManager _deviceManager;
    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;
    private readonly World _world;
    private readonly DefaultParallelRunner _runner;
    private readonly ISystem<GameState> _system;
    private readonly ResolutionIndependentRenderer _resolutionIndependence;
    private readonly Camera _camera;
    private GameState LastState;

    #endregion

    #region Initialisation

    private void InitializeResolutionIndependence(int realScreenWidth, int realScreenHeight)
    {
        // _resolutionIndependence.VirtualWidth = 7680;
        // _resolutionIndependence.VirtualHeight = 4320;
        _resolutionIndependence.VirtualWidth = realScreenWidth;
        _resolutionIndependence.VirtualHeight = realScreenHeight;
        _resolutionIndependence.ScreenWidth = realScreenWidth;
        _resolutionIndependence.ScreenHeight = realScreenHeight;
        _resolutionIndependence.Initialize();

        _camera.RecalculateTransformationMatrices();
    }

    public TestGame()
    {
        _deviceManager = new GraphicsDeviceManager(this);
        IsFixedTimeStep = true;
        _deviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        _deviceManager.IsFullScreen = false;
        _deviceManager.SynchronizeWithVerticalRetrace = true;
        // _deviceManager.PreferredBackBufferWidth = GraphicsDevice.DisplayMode.Width;
        // _deviceManager.PreferredBackBufferHeight = GraphicsDevice.DisplayMode.Height;
        _deviceManager.ApplyChanges();
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        Content.RootDirectory = "Content";
        _resolutionIndependence = new ResolutionIndependentRenderer(this);
        _camera = new Camera(_resolutionIndependence); 
        _batch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("defaultFont");

        _world = new World(100000);
        _runner = new DefaultParallelRunner(Environment.ProcessorCount);
        _system = new SequentialSystem<GameState>(
            new SceneSystem(_world, Content),
            new PlayerInputSystem(_world),
            new PositionSystem(_world, _runner),
            new DrawInfoPositionSystem(_world, _runner),
            new DrawSystem(_resolutionIndependence, _camera, _batch, _world),
            new HudSystem(_resolutionIndependence, _camera, _batch, _font, _world));
    }

    protected override void Initialize()
    {
        _deviceManager.PreferredBackBufferWidth = GraphicsDevice.DisplayMode.Width;
        _deviceManager.PreferredBackBufferHeight = GraphicsDevice.DisplayMode.Height;
        _deviceManager.ApplyChanges();
        InitializeResolutionIndependence(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _camera.Zoom = 0.5f;
        _camera.Position = new Vector2(_resolutionIndependence.VirtualWidth / 2, _resolutionIndependence.VirtualHeight / 2);
        base.Initialize();
    }

    #endregion

    #region Game

    protected override void Update(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.DarkSlateGray);
        var time = (float) gameTime.ElapsedGameTime.TotalSeconds;
        var state = new GameState(time, LastState?.Time ?? time, (float) gameTime.TotalGameTime.TotalSeconds, LastState?.TotalTime ?? (float) gameTime.TotalGameTime.TotalSeconds, Keyboard.GetState());
        _system.Update(state);
        LastState = state;
    }

    protected override void Draw(GameTime gameTime)
    {
    }

    protected override void Dispose(bool disposing)
    {
        _runner.Dispose();
        _world.Dispose();
        _system.Dispose();
        _batch.Dispose();
        _deviceManager.Dispose();

        base.Dispose(disposing);
    }

    #endregion
}