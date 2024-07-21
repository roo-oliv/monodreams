using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Screens;
using MonoDreams.Screen;
using MonoDreams.Renderer;
using MonoDreams.State;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace MonoDreams.Examples;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private ResolutionIndependentRenderer _renderer;
    private Camera _camera;
    private DefaultParallelRunner _runner;
    private ScreenController _screenController;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _graphics.IsFullScreen = false;
        _graphics.SynchronizeWithVerticalRetrace = true;
        _graphics.ApplyChanges();
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.AlphaBlend;
        
        _renderer = new(this);
        _camera = new(_renderer);
        _spriteBatch = new(GraphicsDevice);
        _runner = new(1);
        _screenController = new(_runner, _renderer, _camera, _spriteBatch, Content);
    }
    
    private void InitializeRenderer(int realScreenWidth, int realScreenHeight)
    {
        _renderer.VirtualWidth = realScreenWidth;
        _renderer.VirtualHeight = realScreenHeight;
        _renderer.ScreenWidth = realScreenWidth;
        _renderer.ScreenHeight = realScreenHeight;
        _renderer.Initialize();
        _camera.RecalculateTransformationMatrices();
    }

    protected override void Initialize()
    {
        _graphics.PreferredBackBufferWidth = GraphicsDevice.DisplayMode.Width;
        _graphics.PreferredBackBufferHeight = GraphicsDevice.DisplayMode.Height;
        _graphics.ApplyChanges();
        InitializeRenderer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        _camera.Zoom = 1.0f;
        _camera.Position = Vector2.Zero;

        _screenController.LoadScreen(new MainMenuScreen(GraphicsDevice, Content, _camera, _renderer, _runner, _spriteBatch));
        
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        GraphicsDevice.Clear(Color.OldLace);
        _screenController.Update(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        _screenController.Dispose();
        _runner.Dispose();
        _spriteBatch.Dispose();
        _graphics.Dispose();
        base.Dispose(disposing);
    }
}