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

namespace HeartfeltLending;

public class HeartfeltLending : Game
{
    #region Fields

    private readonly GraphicsDeviceManager _deviceManager;
    private readonly SpriteBatch _batch;
    private World _world;
    private readonly DefaultParallelRunner _runner;
    private ISystem<GameState> _system;
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly Camera _camera;
    private GameState LastState;
    private Texture2D _cursorTexture;

    #endregion

    #region Initialisation

    private void InitializeResolutionIndependence(int realScreenWidth, int realScreenHeight)
    {
        _renderer.VirtualWidth = realScreenWidth;
        _renderer.VirtualHeight = realScreenHeight;
        _renderer.ScreenWidth = realScreenWidth;
        _renderer.ScreenHeight = realScreenHeight;
        _renderer.Initialize();

        _camera.RecalculateTransformationMatrices();
    }

    public HeartfeltLending()
    {
        _deviceManager = new GraphicsDeviceManager(this);
        IsFixedTimeStep = true;
        _deviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        _deviceManager.IsFullScreen = false;
        _deviceManager.SynchronizeWithVerticalRetrace = true;
        _deviceManager.ApplyChanges();
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        Content.RootDirectory = "Content";
        _renderer = new ResolutionIndependentRenderer(this);
        _camera = new Camera(_renderer); 
        _batch = new SpriteBatch(GraphicsDevice);
        _runner = new DefaultParallelRunner(Environment.ProcessorCount);
    }

    protected override void Initialize()
    {
        _deviceManager.PreferredBackBufferWidth = GraphicsDevice.DisplayMode.Width;
        _deviceManager.PreferredBackBufferHeight = GraphicsDevice.DisplayMode.Height;
        _deviceManager.ApplyChanges();
        InitializeResolutionIndependence(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _camera.Zoom = 1f;
        _camera.Position = new Vector2(0, 0);
        
        _cursorTexture = Content.Load<Texture2D>("Other/Transition");
        
        _world = new Menu(GraphicsDevice, Content, _renderer).World;
        
        _system = new SequentialSystem<GameState>(
            new PlayerInputSystem(_world),
            new CursorSystem(_world, _camera, _runner),
            new PositionSystem(_world, _runner),
            new DrawInfoPositionSystem(_world, _runner),
            new DrawSystem(_renderer, _camera, _batch, _world),
            new TextSystem(_camera, _batch, _world));
        
        base.Initialize();
    }

    #endregion

    #region Game

    protected override void Update(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.OldLace);
        // Mouse.SetCursor(MouseCursor.FromTexture2D(_cursorTexture, 0, 0));
        var time = (float) gameTime.ElapsedGameTime.TotalSeconds;
        var state = new GameState(time, LastState?.Time ?? time, (float) gameTime.TotalGameTime.TotalSeconds, LastState?.TotalTime ?? (float) gameTime.TotalGameTime.TotalSeconds, Keyboard.GetState(), Mouse.GetState());
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