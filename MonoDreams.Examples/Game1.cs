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
    private ViewportManager _viewportManager;
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
        
        _viewportManager = new(this);
        _camera = new(_viewportManager.VirtualWidth, _viewportManager.VirtualHeight);
        _spriteBatch = new(GraphicsDevice);
        _runner = new(1);
        _screenController = new(this, _runner, _viewportManager, _camera, _spriteBatch, Content);
    }
    
    private void InitializeRenderer(int realScreenWidth, int realScreenHeight)
    {
        _viewportManager.ScreenWidth = realScreenWidth;
        _viewportManager.ScreenHeight = realScreenHeight;
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

        // _screenController.RegisterScreen(ScreenName.Game, () => new DreamGameScreen(this, GraphicsDevice, Content, _camera, _renderer, _runner, _spriteBatch));
        // _screenController.RegisterScreen(ScreenName.Game, () => new DialogueExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.Game, () => new LoadLevelExampleGameScreen(this, GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.MainMenu, () => new MainMenuScreen(GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));
        _screenController.RegisterScreen(ScreenName.OptionsMenu, () => new OptionsMenuScreen(GraphicsDevice, Content, _camera, _viewportManager, _runner, _spriteBatch));

        _screenController.LoadScreen(ScreenName.Game);
        
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // GraphicsDevice.Clear(Color.OldLace);
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