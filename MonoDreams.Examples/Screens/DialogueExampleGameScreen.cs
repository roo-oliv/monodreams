using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Settings;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;

namespace MonoDreams.Examples.Screens;

public class DialogueExampleGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly DialogueSystem _dialogueSystem;

    public DialogueExampleGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ViewportManager viewportManager, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight) }
        };

        camera.Position = new Vector2(0, 0);

        _world = new World();
        _dialogueSystem = new DialogueSystem(_world, _content, _graphicsDevice, _viewportManager.VirtualWidth, _viewportManager.VirtualHeight);

        // Start yarn dialogue immediately for testing
        _dialogueSystem.StartYarnDialogue("HelloWorld");

        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }

    public void Load(ScreenController screenController, ContentManager content)
    {
    }

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var logicSystems = new SequentialSystem<GameState>(
            new InputMappingSystem(_world),
            new TextUpdateSystem(_world),
            _dialogueSystem
        );

        return new SequentialSystem<GameState>(
            logicSystems
        );
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var pixelPerfectRendering = SettingsManager.Instance.Settings.PixelPerfectRendering;

        var prepDrawSystems = new SequentialSystem<GameState>(
            new CullingSystem(_world, _camera),
            new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering),
            new TextPrepSystem(_world, pixelPerfectRendering)
        );

        var renderSystem = new MasterRenderSystem(
            _spriteBatch,
            _graphicsDevice,
            _camera,
            _renderTargets,
            _world
        );

        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets);

        return new SequentialSystem<GameState>(
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem
        );
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}
