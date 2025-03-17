using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Dialogue;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.InGameDebug;
using MonoDreams.Objects;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Draw;
using MonoDreams.System.Physics;
using MonoDreams.YarnSpinner;
using MonoGame.Extended.BitmapFonts;
using MonoDreams.Examples.Extensions;
using MonoDreams.Examples.Input;

namespace MonoDreams.Examples.Screens;

public class DreamGameScreen : IGameScreen
{
    private readonly ContentManager _content;
    private readonly Game _game;
    private readonly Camera _camera;
    private readonly ResolutionIndependentRenderer _renderer;
    private readonly DefaultParallelRunner _parallelRunner;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly LevelLoader _levelLoader;
    private readonly (RenderTarget2D main, RenderTarget2D ui) _renderTargets;
    
    public DreamGameScreen(Game game, GraphicsDevice graphicsDevice, ContentManager content, Camera camera,
        ResolutionIndependentRenderer renderer, DefaultParallelRunner parallelRunner, SpriteBatch spriteBatch)
    {
        _game = game;
        _content = content;
        _camera = camera;
        _renderer = renderer;
        _parallelRunner = parallelRunner;
        _spriteBatch = spriteBatch;
        _renderTargets = (
            main: new RenderTarget2D(graphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight),
            ui: new RenderTarget2D(graphicsDevice, _renderer.ScreenWidth, _renderer.ScreenHeight)
        );
        
        camera.Position = new Vector2(0, 0);
        
        _world = new World();
        
        // Initialize dialogue manager
        InitializeDialogueManager(graphicsDevice);
        
        _levelLoader = new LevelLoader(_world, graphicsDevice, _content, _spriteBatch, _renderTargets);
        System = CreateSystem();
    }

    private void InitializeDialogueManager(GraphicsDevice graphicsDevice)
    {
        // Load dialogue resources
        var font = _content.Load<BitmapFont>("Fonts/kaph-regular-72px-white-fnt");
        var dialogBox = _content.Load<Texture2D>("Dialouge UI/dialog box");
        var emoteTexture = _content.Load<Texture2D>("Dialouge UI/Emotes/Teemo Basic emote animations sprite sheet")
            .Crop(new Rectangle(0, 0, 32, 32), graphicsDevice);
        
        // Initialize the dialogue manager
        DialogueManager.Instance.Initialize(
            _world,
            font,
            emoteTexture,
            dialogBox,
            _renderTargets.ui,
            graphicsDevice
        );
        
        // Set default character colors
        DialogueManager.Instance.SetCharacterColor("Guide", Color.Yellow);
        
        // Load default emote textures
        DialogueManager.Instance.LoadEmoteTexture("Guide", emoteTexture);
    }

    public ISystem<GameState> System { get; }
    
    public void Load(ScreenController screenController, ContentManager content)
    {
        _levelLoader.LoadLevel(0);
        InGameDebug.Create(_world, _spriteBatch, _renderer);
        
        // Load Yarn scripts
        LoadDialogueContent();
    }
    
    private void LoadDialogueContent()
    {
        try
        {
            Console.WriteLine("[Debug] Loading dialogue content...");
            
            // Load the Yarn programs
            Console.WriteLine("[Debug] Attempting to load hello_world.yarn...");
            var helloWorldProgram = _content.Load<YarnProgram>("Dialogues/hello_world");
            Console.WriteLine("[Debug] hello_world.yarn loaded successfully");
            
            // Check if the program has data
            if (helloWorldProgram.CompiledProgram != null)
            {
                Console.WriteLine($"[Debug] Program has compiled data: {helloWorldProgram.CompiledProgram.Length} bytes");
            }
            else
            {
                Console.WriteLine("[Debug] Warning: Program's compiled data is null!");
            }
            
            // Add them to the dialogue runner
            Console.WriteLine("[Debug] Adding dialogue to DialogueRunner");
            DialogueManager.Instance.GetDialogueRunner().AddStringTable(helloWorldProgram);
            Console.WriteLine("[Debug] Dialogue content loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to load dialogue content: {ex.Message}");
            Console.WriteLine($"[Error] Stack trace: {ex.StackTrace}");
        }
    }
    
    private SequentialSystem<GameState> CreateSystem()
    {
        // Load the font for dialogue
        var dialogueFont = _content.Load<BitmapFont>("Fonts/kaph-regular-72px-white-fnt");
        
        return new SequentialSystem<GameState>(
            new DebugSystem(_world, _game, _spriteBatch),
            new InputHandlingSystem(),
            new MovementSystem(_world, _parallelRunner),
            // new GravitySystem(_world, _parallelRunner, Constants.WorldGravity, Constants.MaxFallVelocity),
            new VelocitySystem(_world, _parallelRunner),
            new CollisionDetectionSystem<CollisionMessage>(_world, _parallelRunner, CollisionMessage.Create),
            new PhysicalCollisionResolutionSystem(_world),
            new DialogueSystem(_world, DialogueManager.Instance.GetDialogueRunner(), new InputState()),
            new PositionSystem(_world, _parallelRunner),
            new BeginDrawSystem(_spriteBatch, _renderer, _camera),
            new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(_renderTargets.main)),
            new ParallelSystem<GameState>(_parallelRunner,
                new DrawSystem(_world, _spriteBatch, _renderTargets.main, _parallelRunner),
                new DynamicTextSystem(_spriteBatch, _world, _renderTargets.main)
                ),
            new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(_renderTargets.ui)),
            new ParallelSystem<GameState>(_parallelRunner,
                new DrawSystem(_world, _spriteBatch, _renderTargets.ui, _parallelRunner),
                new DynamicTextSystem(_spriteBatch, _world, _renderTargets.ui)
            ),
            new ActionSystem<GameState>(_ => _spriteBatch.GraphicsDevice.SetRenderTarget(null)),
            new EndDrawSystem(_spriteBatch)
            // new DrawDebugSystem(_world, _spriteBatch, _renderer)
            );
    }
    
    public void Dispose()
    {
        System.Dispose();
        GC.SuppressFinalize(this);
    }
    
    public enum DrawLayer
    {
        Cursor,
        UIText,
        UIElements,
        Player,
        Level,
        Background,
    }
}
