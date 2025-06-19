using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;

namespace MonoDreams.Examples.Screens;

public class MainMenuScreen : IGameScreen
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    public World World { get; }
    public ISystem<GameState> System { get; }

    public MainMenuScreen(GraphicsDevice graphicsDevice, ContentManager content, Camera camera, ViewportManager renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        // _graphicsDevice = graphicsDevice;
        // _camera = camera;
        // _renderer = renderer;
        // _parallelRunner = parallelRunner;
        // _spriteBatch = spriteBatch;
        // World = new World();
        // System = new SequentialSystem<GameState>(
        //     new PlayerInputSystem(World),
        //     new CursorSystem(World, camera),
        //     new CollisionDetectionSystem(World, _parallelRunner),
        //     new ButtonSystem(World),
        //     new LayoutSystem(World, renderer),
        //     new SizeSystem(World, parallelRunner),
        //     new PositionSystem(World, parallelRunner),
        //     new BeginDrawSystem(spriteBatch, renderer, camera),
        //     new DrawSystem(World, spriteBatch, _parallelRunner),
        //     // new CompositeDrawSystem(spriteBatch, World),
        //     new TextSystem(spriteBatch, World),
        //     new EndDrawSystem(spriteBatch));
    }
    
    public void Load(ScreenController screenController, ContentManager content)
    {
        // // var backgroundImage = content.Load<Texture2D>("buttons/Small Square Buttons");
        // // StaticBackground.Create(World, backgroundImage, _camera, _renderer, drawLayer: DrawLayer.Background);
        //
        // var cursorTexture = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1");
        // Cursor.Create(World, cursorTexture, new Point(42), DrawLayer.Cursor);
        //
        // var font = content.Load<BitmapFont>("Fonts/Kaph Regular White 80px Stroke Black 4px fnt");
        // var rootLayout = World.CreateLayoutEntity(yogaNode: new YogaNode
        // {
        //     Width = _renderer.VirtualWidth,
        //     Height = _renderer.VirtualHeight,
        //     AlignItems = YogaAlign.Center,
        //     JustifyContent = YogaJustify.Center,
        //     Padding = 20,
        // });
        //
        // World.CreateButton(rootLayout, "Start", () => { }, Vector2.Zero, font, Color.Pink, Color.Red, DrawLayer.Buttons);
        // World.CreateButton(rootLayout, "Options", () => { screenController.LoadScreen(ScreenName.OptionsMenu); }, Vector2.Zero, font, Color.Pink, Color.Red, DrawLayer.Buttons);
        // World.CreateButton(rootLayout, "Exit", () => { screenController.Game.Exit(); }, Vector2.Zero, font, Color.Pink, Color.Red, DrawLayer.Buttons);
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
