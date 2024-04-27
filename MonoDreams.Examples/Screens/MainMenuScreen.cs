using System.ComponentModel;
using System.Net.Mime;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Entities;
using MonoDreams.Objects.UI;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Draw;
using MonoGame.Extended;
using MonoGame.Extended.BitmapFonts;
using MonoGame.Extended.Content;
using MonoGame.Extended.TextureAtlases;

namespace MonoDreams.Examples.Screens;

public class MainMenuScreen : IGameScreen
{
    private readonly Camera _camera;
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    public World World { get; }
    public ISystem<GameState> System { get; }

    public MainMenuScreen(Camera camera, ResolutionIndependentRenderer renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        World = new World();
        System = new SequentialSystem<GameState>(
            new PlayerInputSystem(World),
            new CursorSystem(World, camera),
            new CollisionDetectionSystem(World, parallelRunner),
            new ButtonSystem(World),
            new PositionSystem(World, parallelRunner),
            new BeginDrawSystem(spriteBatch, renderer, camera),
            new DrawSystem(spriteBatch, World),
            new CompositeDrawSystem(spriteBatch, World),
            new TextSystem(spriteBatch, World),
            new EndDrawSystem(spriteBatch));
    }
    
    public void Load(ContentManager content)
    {
        // var backgroundImage = content.Load<Texture2D>("buttons/Small Square Buttons");
        // StaticBackground.Create(World, backgroundImage, _camera, _renderer, drawLayer: DrawLayer.Background);
        
        var cursorTexture = content.Load<Texture2D>("Mouse sprites/Triangle Mouse icon 1");
        Cursor.Create(World, cursorTexture, new Point(42), DrawLayer.Cursor);
        
        var font = content.Load<BitmapFont>("Fonts/Kaph-Regular-fnt");
        var buttonTexture = content.Load<Texture2D>("buttons/Square Buttons 26x26");
        Button.Create(
            World,
            "Test",
            () => { },
            Vector2.Zero,
            font,
            Color.Brown,
            Color.Maroon,
            buttonTexture,
            new Thickness(10, 11),
            new Rectangle(0, 0, 100, 100),
            DrawLayer.Buttons);
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
