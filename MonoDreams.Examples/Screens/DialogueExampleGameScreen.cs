using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Dialogue;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;

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
    private readonly LevelLoader _levelLoader;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    
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
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _camera.VirtualWidth, _camera.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _camera.VirtualWidth, _camera.VirtualHeight) }
        };
        
        camera.Position = new Vector2(0, 0);
        
        _world = new World();
        // _levelLoader = new LevelLoader(_world, graphicsDevice, _content, _spriteBatch, _renderTargets);
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }

    public void Load(ScreenController screenController, ContentManager content)
    {
        // _levelLoader.LoadLevel(0);
    }
    
    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        // Systems that modify component state (can often be parallel)
        var logicSystems = new ParallelSystem<GameState>(_parallelRunner,
            // new InputHandlingSystem(),
            // new MovementSystem(),
            // new VelocitySystem(),
            // new CollisionDetectionSystem(),
            // new PhysicalCollisionResolutionSystem(),
            // new PositionSystem(),
            new TextUpdateSystem(_world), // Logic only
            new DialogueUpdateSystem(_world)
            // ... other game logic systems
        );

        // --- Example Setup for Dialogue UI Entity ---
        // This would typically happen elsewhere (e.g., UI manager, game state load)
        var dialogueUIEntity = _world.CreateEntity();
        dialogueUIEntity.Set(new Transform { CurrentPosition = new Vector2(20, 0) });
        dialogueUIEntity.Set(new DrawComponent());
        dialogueUIEntity.Set(new DialogueUIStateComponent {
            IsActive = true,
            CurrentText = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
            DialogueFont = _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt"), // Load assets
            TextColor = Color.SaddleBrown,
            DialogueBoxTexture = _content.Load<Texture2D>("Dialouge UI/dialog box"),
            DialogueBoxNinePatch = new NinePatchInfo(),
            DialogueBoxSize = new Vector2(_graphicsDevice.Viewport.Width - 200, 180), // Example size
            SpeakerEmoteTexture = _content.Load<Texture2D>("Dialouge UI/Emotes/Teemo Basic emote animations sprite sheet").Crop(new Rectangle(0, 0, 32, 32), _graphicsDevice),
            NextIndicatorTexture = _content.Load<Texture2D>("Dialouge UI/dialog box character finished talking click to continue indicator - spritesheet").Crop(new Rectangle(96, 0, 16, 16), _graphicsDevice),
            DialogueBoxOffset = new Vector2(140, 0),
            EmoteOffset = new Vector2(0, 40),
            TextOffset = new Vector2(180, 30),
            TextArea = new Rectangle(180, 30, _graphicsDevice.Viewport.Width - 200 - 160, 120), // Example text area bounds
            NextIndicatorOffset = new Vector2(_graphicsDevice.Viewport.Width - 180, 100), // Bottom right approx
            TextRevealingSpeed = 20,
            IsTextFullyRevealed = false,
            VisibleCharacterCount = 0,
            TextRevealStartTime = float.NaN,
        });

        return new SequentialSystem<GameState>(
            // new DebugSystem(_world, _game, _spriteBatch), // If needed
            logicSystems
        );
    }
    
    private SequentialSystem<GameState> CreateDrawSystem()
    {
        // Systems that prepare DrawComponent based on state (can often be parallel)
        var prepDrawSystems = new SequentialSystem<GameState>( // Or parallel if clearing is handled carefully
            // Optional: A system to clear all DrawComponents first?
            // new ClearDrawComponentSystem(_world),
            new CullingSystem(_world, _camera),
            new DialogueUIRenderPrepSystem(_world),
            new SpritePrepSystem(_world, _graphicsDevice),
            new TextPrepSystem(_world)
            // ... other systems preparing DrawElements (UI, particles, etc.)
        );

        // The single system that handles all rendering (strictly sequential)
        var renderSystem = new MasterRenderSystem(
            _spriteBatch,
            _graphicsDevice,
            _camera,
            _renderTargets, // Pass the dictionary/collection of RTs
            _world,
            _viewportManager
        );
    
        // Final system to draw RenderTargets to backbuffer (if needed)
        var finalDrawToScreenSystem = new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets);
        
        return new SequentialSystem<GameState>(
            prepDrawSystems,
            renderSystem,
            finalDrawToScreenSystem // Draw RTs to screen
            // new DrawDebugSystem(_world, _spriteBatch, _renderer) // If needed
        );
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}