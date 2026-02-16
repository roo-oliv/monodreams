using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Dialogue;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Message;
using MonoDreams.State;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;

namespace MonoDreams.Examples.System.Dialogue;

public class DialogueSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly Entity _rootEntity;
    private readonly DialogueState _dialogueState;

    public bool IsEnabled { get; set; } = true;

    public DialogueSystem(World world, ContentManager content, GraphicsDevice graphicsDevice, int virtualWidth, int virtualHeight)
    {
        _world = world;
        world.Subscribe(this);

        // Load assets
        var dialogBoxTexture = content.Load<Texture2D>("Dialouge UI/dialog box medium");
        var font = content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");
        var indicatorSheet = content.Load<Texture2D>("Dialouge UI/dialog box character finished talking click to continue indicator - spritesheet");
        var indicatorTexture = indicatorSheet.Crop(new Rectangle(96, 0, 16, 16), graphicsDevice);

        // Layout constants (UI coordinates, virtual resolution)
        var boxWidth = virtualWidth - 40;
        var boxHeight = 120;
        var rootPosition = new Vector2(20, virtualHeight - boxHeight - 20);
        var textOffset = new Vector2(16, 16);
        var indicatorOffset = new Vector2(boxWidth - 28, boxHeight - 24);

        // Create root entity
        var rootTransform = new Transform(rootPosition);
        _rootEntity = world.CreateEntity();
        _rootEntity.Set(new EntityInfo(EntityType.Interface));
        _rootEntity.Set(rootTransform);
        _dialogueState = new DialogueState();
        _rootEntity.Set(_dialogueState);

        // Create box child entity
        _dialogueState.BoxEntity = world.CreateEntity();
        _dialogueState.BoxEntity.Set(new EntityInfo(EntityType.Interface));
        _dialogueState.BoxEntity.Set(new Transform { Parent = rootTransform });
        _dialogueState.BoxEntity.Set(new SpriteInfo
        {
            SpriteSheet = dialogBoxTexture,
            Source = new Rectangle(0, 0, dialogBoxTexture.Width, dialogBoxTexture.Height),
            Size = new Vector2(boxWidth, boxHeight),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = 0.05f,
            NinePatchData = new NinePatchInfo(
                23,
                new Rectangle(0, 0, 23, 23),
                new Rectangle(23, 0, 1, 23),
                new Rectangle(24, 0, 23, 23),
                new Rectangle(0, 23, 23, 1),
                new Rectangle(23, 23, 1, 1),
                new Rectangle(24, 23, 23, 1),
                new Rectangle(0, 24, 23, 23),
                new Rectangle(23, 24, 1, 23),
                new Rectangle(24, 24, 23, 23))
        });
        _dialogueState.BoxEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.UI
        });

        // Create text child entity
        _dialogueState.TextEntity = world.CreateEntity();
        _dialogueState.TextEntity.Set(new EntityInfo(EntityType.Interface));
        _dialogueState.TextEntity.Set(new Transform(textOffset) { Parent = rootTransform });
        _dialogueState.TextEntity.Set(new DynamicText
        {
            Target = RenderTargetID.UI,
            LayerDepth = 0.1f,
            Font = font,
            Color = Color.SaddleBrown,
            Scale = 0.5f,
            RevealingSpeed = 20,
            RevealStartTime = float.NaN,
            IsRevealed = false,
            VisibleCharacterCount = 0,
            TextContent = ""
        });
        _dialogueState.TextEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Text,
            Target = RenderTargetID.UI
        });

        // Create indicator child entity
        _dialogueState.IndicatorEntity = world.CreateEntity();
        _dialogueState.IndicatorEntity.Set(new EntityInfo(EntityType.Interface));
        _dialogueState.IndicatorEntity.Set(new Transform(indicatorOffset) { Parent = rootTransform });
        _dialogueState.IndicatorEntity.Set(new SpriteInfo
        {
            SpriteSheet = indicatorTexture,
            Source = new Rectangle(0, 0, indicatorTexture.Width, indicatorTexture.Height),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = 0.1f
        });
        _dialogueState.IndicatorEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.UI
        });
    }

    [Subscribe]
    private void OnCollision(in CollisionMessage message)
    {
        if (message.Type != CollisionType.Dialogue) return;
        if (_dialogueState.IsActive || _dialogueState.WasTriggered) return;

        ActivateDialogue("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.");
    }

    public void ActivateDialogue(string text)
    {
        _dialogueState.IsActive = true;
        _dialogueState.WasTriggered = true;

        // Show box
        _dialogueState.BoxEntity.Set<Visible>();

        // Set text content and reset reveal
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicText>();
        dynamicText.TextContent = text;
        dynamicText.VisibleCharacterCount = 0;
        dynamicText.IsRevealed = false;
        dynamicText.RevealStartTime = float.NaN;

        // Hide indicator until text is fully revealed
        if (_dialogueState.IndicatorEntity.Has<Visible>())
            _dialogueState.IndicatorEntity.Remove<Visible>();

        _world.Publish(new DialogueActiveMessage(true));
    }

    public void Update(GameState state)
    {
        if (!_dialogueState.IsActive) return;

        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicText>();

        // Show/hide continue indicator based on text reveal
        if (dynamicText.IsRevealed)
        {
            if (!_dialogueState.IndicatorEntity.Has<Visible>())
                _dialogueState.IndicatorEntity.Set<Visible>();
        }
        else
        {
            if (_dialogueState.IndicatorEntity.Has<Visible>())
                _dialogueState.IndicatorEntity.Remove<Visible>();
        }

        // Check for dismiss input
        if (InputState.Interact.Pressed(state))
        {
            DeactivateDialogue();
        }
    }

    private void DeactivateDialogue()
    {
        _dialogueState.IsActive = false;

        // Hide box
        if (_dialogueState.BoxEntity.Has<Visible>())
            _dialogueState.BoxEntity.Remove<Visible>();

        // Clear box draw component
        var boxDraw = _dialogueState.BoxEntity.Get<DrawComponent>();
        boxDraw.Texture = null;

        // Clear text
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicText>();
        dynamicText.VisibleCharacterCount = 0;
        dynamicText.IsRevealed = false;
        dynamicText.TextContent = "";

        var textDraw = _dialogueState.TextEntity.Get<DrawComponent>();
        textDraw.Text = null;
        textDraw.Font = null;

        // Hide indicator
        if (_dialogueState.IndicatorEntity.Has<Visible>())
            _dialogueState.IndicatorEntity.Remove<Visible>();

        var indicatorDraw = _dialogueState.IndicatorEntity.Get<DrawComponent>();
        indicatorDraw.Texture = null;

        _world.Publish(new DialogueActiveMessage(false));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
