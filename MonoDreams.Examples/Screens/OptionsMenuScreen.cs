using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Objects.UI;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Draw;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.Screens;

public class OptionsMenuScreen : IGameScreen
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    public World World { get; }
    public ISystem<GameState> System { get; }

    public OptionsMenuScreen(GraphicsDevice graphicsDevice, ContentManager content, Camera camera, ResolutionIndependentRenderer renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _graphicsDevice = graphicsDevice;
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        World = new World();
        System = new SequentialSystem<GameState>(
            new PlayerInputSystem(World),
            new CursorSystem(World, camera),
            new CollisionDetectionSystem(World, _parallelRunner),
            new ButtonSystem(World),
            new PositionSystem(World, parallelRunner),
            new BeginDrawSystem(spriteBatch, renderer, camera),
            new DrawSystem(World, spriteBatch, _parallelRunner),
            // new CompositeDrawSystem(spriteBatch, World),
            new TextSystem(spriteBatch, World),
            new EndDrawSystem(spriteBatch));
    }
    
    public void Load(ScreenController screenController, ContentManager content)
    {
        // var backgroundImage = content.Load<Texture2D>("buttons/Small Square Buttons");
        // StaticBackground.Create(World, backgroundImage, _camera, _renderer, drawLayer: DrawLayer.Background);
        
        var cursorTexture = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1");
        Cursor.Create(World, cursorTexture, new Point(42), DrawLayer.Cursor);
        
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
        System.Dispose();
        GC.SuppressFinalize(this);
    }
    
    public enum DrawLayer
    {
        Cursor,
        Buttons,
        Background,
    }
}
