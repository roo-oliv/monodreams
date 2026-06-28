using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System.Draw;

namespace MonoDreams.Spike.Web
{
    /// <summary>
    /// Phase 0 proof: draw ONE sprite through the real MonoDreams render pipeline, on the KNI
    /// BlazorGL backend, in the browser. This deliberately uses the actual engine types
    /// (DefaultEcs World, TransformComponent, SpriteInfoComponent, DrawComponent, Camera,
    /// CullingSystem, SpritePrepSystem, MasterRenderSystem, FinalDrawSystem) so the spike
    /// exercises both halves of the plan's hypothesis at once:
    ///   1. MonoDreams source recompiles unchanged against nkast.Xna.Framework.* (the spike lib).
    ///   2. That recompiled pipeline actually renders pixels on BlazorGL/WebGL in Chrome.
    ///
    /// The sprite texture is generated procedurally at runtime (a 16x16 checker), so the spike
    /// has NO content-pipeline dependency — that (.mgcb/.xnb for web) is Phase 3 work and out of
    /// scope here. GraphicsProfile is left at the BlazorGL default (Reach-class on WebGL); the
    /// HiDef question from the plan is deferred to the shader-bearing phases.
    /// </summary>
    public class SpikeGame : Game
    {
        private const int VirtualWidth = 800;
        private const int VirtualHeight = 600;

        private readonly GraphicsDeviceManager _graphics;

        private World _world;
        private GameState _gameState;
        private SpriteBatch _spriteBatch;
        private ViewportManager _viewportManager;
        private Camera _camera;

        private RenderTarget2D _mainTarget;
        private SequentialSystem<GameState> _pipeline;
        private FinalDrawSystem _finalDraw;

        private Texture2D _spriteSheet;

        public SpikeGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            // No Content.RootDirectory / ContentManager use: the sprite is procedural.
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            Console.WriteLine("[SPIKE] Initialize begin");
            _graphics.PreferredBackBufferWidth = VirtualWidth;
            _graphics.PreferredBackBufferHeight = VirtualHeight;
            _graphics.ApplyChanges();

            base.Initialize();
            Console.WriteLine("[SPIKE] Initialize end");
        }

        protected override void LoadContent()
        {
            base.LoadContent();

            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _viewportManager = new ViewportManager(this, VirtualWidth, VirtualHeight)
            {
                ScreenWidth = GraphicsDevice.PresentationParameters.BackBufferWidth,
                ScreenHeight = GraphicsDevice.PresentationParameters.BackBufferHeight,
            };

            _camera = new Camera(VirtualWidth, VirtualHeight);

            _mainTarget = new RenderTarget2D(
                GraphicsDevice, VirtualWidth, VirtualHeight,
                false, SurfaceFormat.Color, DepthFormat.None);

            _spriteSheet = CreateCheckerTexture(16, 16, Color.OrangeRed, Color.Gold);

            _world = new World();
            _gameState = new GameState(new GameTime());

            // One sprite entity, centered in the virtual screen, on the Main target. The standard
            // renderable stack per CORE_TENETS: EntityInfo + Transform + SpriteInfo + DrawComponent.
            // VisibleComponent is added by CullingSystem (we must NOT set it ourselves on Main).
            var sprite = _world.CreateEntity();
            sprite.Set(new EntityInfoComponent("SpikeSprite"));
            sprite.Set(new TransformComponent(new Vector2(VirtualWidth / 2f - 128, VirtualHeight / 2f - 128)));
            sprite.Set(new SpriteInfoComponent
            {
                SpriteSheet = _spriteSheet,
                Source = new Rectangle(0, 0, 16, 16),
                Size = new Vector2(256, 256),
                Color = Color.White,
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f,
            });
            sprite.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });

            // Real MonoDreams render pipeline, in the reference order (cull -> prep -> render).
            _pipeline = new SequentialSystem<GameState>(
                new CullingSystem(_world, _camera),
                new SpritePrepSystem(_world, GraphicsDevice, pixelPerfectRendering: false),
                new MasterRenderSystem(
                    _spriteBatch, GraphicsDevice, _world,
                    RenderTargetID.Main, _mainTarget, _camera));

            // FinalDrawSystem composites the Main target onto the back buffer.
            _finalDraw = new FinalDrawSystem(
                _spriteBatch, GraphicsDevice, _viewportManager,
                new List<RenderLayer> { RenderLayer.Main(_mainTarget) });

            Console.WriteLine($"[SPIKE] LoadContent done. backbuffer={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}");
        }

        private int _frame;

        protected override void Update(GameTime gameTime)
        {
            // Keep the camera looking at the center of the virtual screen so the sprite is on-screen.
            _camera.Position = new Vector2(VirtualWidth / 2f, VirtualHeight / 2f);

            _gameState.Update(gameTime);
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Render the world to the Main target, then composite to the back buffer.
            _pipeline.Update(_gameState);
            _finalDraw.Update(_gameState);

            if (_frame < 5)
                Console.WriteLine($"[SPIKE] Draw frame {_frame}");
            _frame++;

            base.Draw(gameTime);
        }

        private Texture2D CreateCheckerTexture(int width, int height, Color a, Color b)
        {
            var tex = new Texture2D(GraphicsDevice, width, height);
            var data = new Color[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                data[y * width + x] = ((x / 4 + y / 4) % 2 == 0) ? a : b;
            tex.SetData(data);
            return tex;
        }
    }
}
