using System;
using System.IO;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Level;
using MonoDreams.State;
using MonoDreams.System;

namespace MonoDreams;

public class Game1 : Game
{
    #region Fields

    private readonly GraphicsDeviceManager _deviceManager;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _square;
    private readonly SpriteFont _font;
    private readonly World _world;
    private readonly DefaultParallelRunner _runner;
    private readonly ISystem<GameState> _system;
    private GameState LastState;

    #endregion

    #region Initialisation

    public Game1()
    {
        _deviceManager = new GraphicsDeviceManager(this);
        IsFixedTimeStep = true;
        _deviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        _deviceManager.IsFullScreen = false;
        _deviceManager.PreferredBackBufferWidth = 1000;
        _deviceManager.PreferredBackBufferHeight = 700;
        _deviceManager.ApplyChanges();
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        Content.RootDirectory = "Content";

        _batch = new SpriteBatch(GraphicsDevice);
        _square = Content.Load<Texture2D>("square");
        _font = Content.Load<SpriteFont>("defaultFont");
        // using (Stream stream = File.OpenRead(@"Content/square.png"))
        // {
        //     _square = Texture2D.FromStream(GraphicsDevice, stream);
        // }

        _world = new World(10000);

        _runner = new DefaultParallelRunner(Environment.ProcessorCount);
        _system = new SequentialSystem<GameState>(
            new SceneSystem(_world),
            new PlayerInputSystem(_world),
            new PlayerMovementSystem(_world, _runner),
            new DynamicBodySystem(_world, _runner),
            new CollisionDetectionSystem(_world),
            new CollisionDrawSystem(_batch, _square, _font, _world),
            //new DynamicBodyCollisionResolutionSystem(_world),  // TODO: Make this an event system to resolve collisions in order!
            new CollisionResolutionSystem(_world),
            new PositionSystem(_world, _runner),
            new DrawSystem(_batch, _square, _world),
            new HudSystem(_batch, _font, _world));

        Level2.CreatePlayer(_world);
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
        _square.Dispose();
        _batch.Dispose();
        _deviceManager.Dispose();

        base.Dispose(disposing);
    }

    #endregion
}