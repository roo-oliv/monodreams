using DefaultEcs.System;
using DefaultEcs.Threading;
using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Dialogue;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.Examples.System.Draw;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;
using DefaultEcsWorld = DefaultEcs.World;
using static MonoDreams.Examples.System.SystemPhase;

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
    private readonly DefaultEcsWorld _defaultEcsWorld;
    private World _world;
    private Pipeline _updatePipeline;
    private Pipeline _drawPipeline;
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
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight) }
        };

        camera.Position = new Vector2(0, 0);

        _world = World.Create();
        _defaultEcsWorld = new DefaultEcsWorld();

        RegisterSystems();
        _updatePipeline = CreateUpdatePipeline();
        _drawPipeline = CreateDrawPipeline();

        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    private void RegisterSystems()
    {
        // Render Phase Systems
        CullingSystem.Register(_world, _camera);
        // DialogueUIRenderPrepSystem.Register(_world);
        SpritePrepSystem.Register(_world, _graphicsDevice);
        TextPrepSystem.Register(_world);
        MasterRenderSystem.Register(_world, _spriteBatch, _graphicsDevice, _camera, _renderTargets);
        FinalDrawSystem.Register(_world, _spriteBatch, _graphicsDevice, _viewportManager, _renderTargets);
    }

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }

    public void Update(GameTime gameTime)
    {
        var gameState = new GameState(gameTime);
        UpdateSystem.Update(gameState);
        _world.RunPipeline(_updatePipeline, (float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public void Draw(GameTime gameTime)
    {
        var gameState = new GameState(gameTime);
        DrawSystem.Update(gameState);
        _world.RunPipeline(_drawPipeline, (float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        // --- Example Setup for Dialogue UI Entity ---
        var dialogueUIEntity = _defaultEcsWorld.CreateEntity();
        dialogueUIEntity.Set(new Position { Current = new Vector2(20, 0) });
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

        // Create corresponding Flecs entity
        _world.Entity()
            .Set(new Position { Current = new Vector2(20, 0) })
            .Set(new DrawComponent())
            .Set(new DialogueUIStateComponent {
                IsActive = true,
                CurrentText = "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
                DialogueFont = _content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt"),
                TextColor = Color.SaddleBrown,
                DialogueBoxTexture = _content.Load<Texture2D>("Dialouge UI/dialog box"),
                DialogueBoxNinePatch = new NinePatchInfo(),
                DialogueBoxSize = new Vector2(_graphicsDevice.Viewport.Width - 200, 180),
                SpeakerEmoteTexture = _content.Load<Texture2D>("Dialouge UI/Emotes/Teemo Basic emote animations sprite sheet").Crop(new Rectangle(0, 0, 32, 32), _graphicsDevice),
                NextIndicatorTexture = _content.Load<Texture2D>("Dialouge UI/dialog box character finished talking click to continue indicator - spritesheet").Crop(new Rectangle(96, 0, 16, 16), _graphicsDevice),
                DialogueBoxOffset = new Vector2(140, 0),
                EmoteOffset = new Vector2(0, 40),
                TextOffset = new Vector2(180, 30),
                TextArea = new Rectangle(180, 30, _graphicsDevice.Viewport.Width - 200 - 160, 120),
                NextIndicatorOffset = new Vector2(_graphicsDevice.Viewport.Width - 180, 100),
                TextRevealingSpeed = 20,
                IsTextFullyRevealed = false,
                VisibleCharacterCount = 0,
                TextRevealStartTime = float.NaN,
            });
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
            new TextUpdateSystem(_defaultEcsWorld), // Logic only
            new DialogueUpdateSystem(_defaultEcsWorld)
            // ... other game logic systems
        );

        return new SequentialSystem<GameState>(
            // new DebugSystem(_world, _game, _spriteBatch), // If needed
            logicSystems
        );
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        // Return empty system as we're using Flecs pipelines instead
        return new SequentialSystem<GameState>();
    }

    private Pipeline CreateUpdatePipeline()
    {
        return _world.Pipeline()
            .With(Ecs.System)
            .Without(DrawPhase)
            .Build();
    }

    private Pipeline CreateDrawPipeline()
    {
        return _world.Pipeline()
            .With(Ecs.System)
            .With(DrawPhase)
            .Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        GC.SuppressFinalize(this);
    }
}