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

namespace MonoDreams
{
    public class Game1 : Game
    {
        #region Fields

        private readonly GraphicsDeviceManager _deviceManager;
        private readonly SpriteBatch _batch;
        private readonly Texture2D _square;
        private readonly World _world;
        private readonly DefaultParallelRunner _runner;
        private readonly ISystem<GameState> _system;

        #endregion

        #region Initialisation

        public Game1()
        {
            _deviceManager = new GraphicsDeviceManager(this);
            IsFixedTimeStep = true;
            _deviceManager.GraphicsProfile = GraphicsProfile.HiDef;
            _deviceManager.IsFullScreen = false;
            _deviceManager.PreferredBackBufferWidth = 800;
            _deviceManager.PreferredBackBufferHeight = 600;
            _deviceManager.ApplyChanges();
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            Content.RootDirectory = "Content";

            _batch = new SpriteBatch(GraphicsDevice);
            _square = Content.Load<Texture2D>("square");
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
                new VelocitySystem(_world, _runner),
                new CollisionSystem(_world),
                new BallBoundSystem(_world),
                new PositionSystem(_world, _runner),
                new DrawSystem(_batch, _square, _world));

            Level1.CreatePlayer(_world);
        }

        #endregion

        #region Game

        protected override void Update(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            var state = new GameState((float) gameTime.ElapsedGameTime.TotalSeconds, Keyboard.GetState());
            _system.Update(state);
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
}