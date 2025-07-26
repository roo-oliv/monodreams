using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Message;
using MonoDreams.Objects.UI;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Screens;

public class OptionsMenuScreen : IGameScreen
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly (RenderTarget2D main, RenderTarget2D ui) _renderTargets;
    public World World { get; }
    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }

    public OptionsMenuScreen(GraphicsDevice graphicsDevice, ContentManager content, Camera camera, ViewportManager renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _graphicsDevice = graphicsDevice;
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = (
            main: new RenderTarget2D(_graphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight),
            ui: new RenderTarget2D(_graphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight)
        );
        
        World = new World();
        // UpdateSystem = new SequentialSystem<GameState>(
        //     new PlayerInputSystem(World),
        //     new CursorSystem(World, camera),
        //     new CollisionDetectionSystem<CollisionMessage>(World, _parallelRunner, CollisionMessage.Create),
        //     new ButtonSystem<CollisionMessage>(World),
        //     new PositionSystem(World, parallelRunner),
        //     new BeginDrawSystem(spriteBatch, camera),
        //     new DrawSystem(World, spriteBatch, _renderTargets.main, _parallelRunner),
        //     // new CompositeDrawSystem(spriteBatch, World),
        //     new TextSystem(spriteBatch, World),
        //     new EndDrawSystem(spriteBatch));
    }
    
    public void Load(ScreenController screenController, ContentManager content)
    {
        // var backgroundImage = content.Load<Texture2D>("buttons/Small Square Buttons");
        // StaticBackground.Create(World, backgroundImage, _camera, _renderer, drawLayer: DrawLayer.Background);
        
        var cursorTexture = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1");
        Cursor.Create(_renderTargets.main, World, cursorTexture, new Point(42), DrawLayer.Cursor);
        
        var font = content.Load<BitmapFont>("Fonts/Kaph Regular White 80px Stroke Black 4px fnt");Button.Create(
            World,
            "Foo",
            () => { },
            new Vector2(0, -120),
            font,
            Color.Pink,
            Color.Red,
            drawLayer: DrawLayer.Buttons);
        Button.Create(
            World,
            "Bar",
            () => { },
            Vector2.Zero,
            font,
            Color.Pink,
            Color.Red,
            drawLayer: DrawLayer.Buttons);
        Button.Create(
            World,
            "Baaaaack",
            () => { screenController.LoadScreen(ScreenName.MainMenu); },
            new Vector2(0, 120),
            font,
            Color.Pink,
            Color.Red,
            drawLayer: DrawLayer.Buttons);
    }

    public void Dispose()
    {
        World.Dispose();
        UpdateSystem.Dispose();
        GC.SuppressFinalize(this);
    }
    
    public enum DrawLayer
    {
        Cursor,
        Buttons,
        Background,
    }
}
