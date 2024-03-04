using System;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoGame.ImGuiNet;
using ButtonState = MonoDreams.Component.ButtonState;

namespace HeartfeltLending;

public class HeartfeltLending : Game
{
    #region Fields

    private static readonly bool IsDebug = true;
    private readonly GraphicsDeviceManager _deviceManager;
    private readonly SpriteBatch _batch;
    private World _world;
    private readonly DefaultParallelRunner _runner;
    private ISystem<GameState> _system;
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly Camera _camera;
    private GameState LastState;
    private Texture2D _cursorTexture;
    private readonly Texture2D _square;
    private readonly SpriteFont _font;
    private RenderTarget2D _debugRenderTarget;
    private ImGuiRenderer _guiRenderer;

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
        _square = Content.Load<Texture2D>("square");
        _font = Content.Load<SpriteFont>("defaultFont");
    }

    protected override void Initialize()
    {
        _deviceManager.PreferredBackBufferWidth = GraphicsDevice.DisplayMode.Width;
        _deviceManager.PreferredBackBufferHeight = GraphicsDevice.DisplayMode.Height;
        _deviceManager.ApplyChanges();
        InitializeResolutionIndependence(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _camera.Zoom = 1f;
        _camera.Position = new Vector2(0, 0);
        
        _debugRenderTarget = new RenderTarget2D(
            _batch.GraphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight);
        _guiRenderer = new ImGuiRenderer(this);
        
        _cursorTexture = Content.Load<Texture2D>("Other/Transition");
        
        _world = new Menu(GraphicsDevice, Content, _renderer).World;
        
        _system = new SequentialSystem<GameState>(
            new PlayerInputSystem(_world),
            new CursorSystem(_world, _camera, _runner),
            new CollisionDetectionSystem(_world, _runner),
            new CollisionDrawSystem(_square, _world),
            new ButtonSystem(_world),
            new PositionSystem(_world, _runner),
            new DrawSystem(_renderer, _camera, _batch, _world),
            new CollidableDrawSystem(_camera, _batch, _world, _runner),
            new TextSystem(_camera, _batch, _world),
            new DebugInfoSystem(_renderer, _camera, _batch, _font, _world));
        
        base.Initialize();
    }

    #endregion

    #region Game

    protected override void Update(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.OldLace);
        // Mouse.SetCursor(MouseCursor.FromTexture2D(_cursorTexture, 0, 0));
        var time = (float) gameTime.ElapsedGameTime.TotalSeconds;
        var state = new GameState(gameTime, time, LastState?.Time ?? time, (float) gameTime.TotalGameTime.TotalSeconds, LastState?.TotalTime ?? (float) gameTime.TotalGameTime.TotalSeconds, Keyboard.GetState(), Mouse.GetState());

        if (IsDebug)
        {
            SetupDebugGui(gameTime);
        }

        _system.Update(state);
        LastState = state;
        
        if (IsDebug)
        {
            DrawDebugGui();
        }
    }

    private void SetupDebugGui(GameTime gameTime)
    {
        _guiRenderer.RebuildFontAtlas();
        _batch.GraphicsDevice.SetRenderTarget(_debugRenderTarget);
        _batch.GraphicsDevice.Clear(Color.Transparent);
        _guiRenderer.BeforeLayout(gameTime);

        var cursorEntity = _world.GetEntities().With<CursorController>().AsEnumerable().First();
        ImGui.Begin("Cursor", ImGuiWindowFlags.Modal);
        ImGui.Text($"Position: {cursorEntity.Get<Position>().CurrentLocation}");
        ImGui.End();
        
        var buttons = _world.GetEntities().With<ButtonState>().AsEnumerable();
        ImGui.Begin("Buttons", ImGuiWindowFlags.Modal);
        foreach (var button in buttons)
        {
            if (ImGui.TreeNode($"Button: {button.Get<Text>().Value}"))
            {
                ImGui.Text($"Position: {button.Get<Position>().CurrentLocation}");
                ImGui.Text($"Bounds: {button.Get<Collidable>().Bounds}");
                ImGui.Text($"Selected: {button.Get<ButtonState>().Selected}");
                ImGui.Text($"Pressed: {button.Get<ButtonState>().Pressed}");
                ImGui.TreePop();
            }
        }
        ImGui.End();
        
        _guiRenderer.AfterLayout();
        _batch.GraphicsDevice.SetRenderTarget(null);
    }

    private void DrawDebugGui()
    {
        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        _batch.Draw(_debugRenderTarget, Vector2.Zero, new Rectangle(0, 0, _renderer.ScreenWidth, _renderer.ScreenHeight), Color.White);
        _batch.End();
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