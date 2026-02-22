using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Dialogue;
using MonoDreams.Examples.Draw;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Dialogue;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Message;
using MonoDreams.Message;
using MonoDreams.Extension;
using MonoDreams.State;
using MonoDreams.Util;
using MonoDreams.YarnSpinner;
using MonoGame.Extended.BitmapFonts;
using Yarn;
using DynamicText = MonoDreams.Component.Draw.DynamicText;

namespace MonoDreams.Examples.System.Dialogue;

public class DialogueSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly Entity _rootEntity;
    private readonly DialogueState _dialogueState;
    private readonly BitmapFont _font;
    private readonly Transform _rootTransform;
    private readonly DrawLayerMap _layers;

    // Yarn runtime
    private readonly Yarn.Dialogue _yarnDialogue;
    private readonly DialogueRunner _dialogueRunner;
    private readonly InMemoryVariableStorage _variableStorage;

    public bool IsEnabled { get; set; } = true;

    public DialogueSystem(World world, ContentManager content, GraphicsDevice graphicsDevice, int virtualWidth, int virtualHeight, DrawLayerMap layers, IEnumerable<string> dialogueContentPaths = null)
    {
        _world = world;
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        world.Subscribe(this);

        // Load assets
        var dialogBoxTexture = content.Load<Texture2D>("Dialouge UI/dialog box medium");
        _font = content.Load<BitmapFont>("Fonts/PPMondwest-Regular-fnt");
        var indicatorSheet = content.Load<Texture2D>("Dialouge UI/dialog box character finished talking click to continue indicator - spritesheet");
        var indicatorTexture = indicatorSheet.Crop(new Rectangle(96, 0, 16, 16), graphicsDevice);

        // Layout constants (UI coordinates, virtual resolution)
        var boxWidth = virtualWidth - 40;
        var boxHeight = 120;
        var rootPosition = new Vector2(20, virtualHeight - boxHeight - 20);
        var textOffset = new Vector2(16, 16);
        var indicatorOffset = new Vector2(boxWidth - 28, boxHeight - 24);

        // Create root entity
        _rootTransform = new Transform(rootPosition);
        _rootEntity = world.CreateEntity();
        _rootEntity.Set(new EntityInfo(nameof(EntityType.Interface), "DialogueRoot"));
        _rootEntity.Set(_rootTransform);
        _dialogueState = new DialogueState();
        _rootEntity.Set(_dialogueState);

        // Create box child entity
        _dialogueState.BoxEntity = world.CreateEntity();
        _dialogueState.BoxEntity.Set(new EntityInfo(nameof(EntityType.Interface), "DialogueBox"));
        _dialogueState.BoxEntity.Set(new Transform());
        _dialogueState.BoxEntity.SetParent(_rootEntity);
        _dialogueState.BoxEntity.Set(new SpriteInfo
        {
            SpriteSheet = dialogBoxTexture,
            Source = new Rectangle(0, 0, dialogBoxTexture.Width, dialogBoxTexture.Height),
            Size = new Vector2(boxWidth, boxHeight),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = _layers.GetDepth(GameDrawLayer.DialogueUI),
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
        _dialogueState.TextEntity.Set(new EntityInfo(nameof(EntityType.Interface), "DialogueText"));
        _dialogueState.TextEntity.Set(new Transform(textOffset));
        _dialogueState.TextEntity.SetParent(_rootEntity);
        _dialogueState.TextEntity.Set(new DynamicText
        {
            Target = RenderTargetID.UI,
            LayerDepth = _layers.GetDepth(GameDrawLayer.DialogueUI, +0.01f),
            Font = _font,
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
        _dialogueState.IndicatorEntity.Set(new EntityInfo(nameof(EntityType.Interface), "DialogueIndicator"));
        _dialogueState.IndicatorEntity.Set(new Transform(indicatorOffset));
        _dialogueState.IndicatorEntity.SetParent(_rootEntity);
        _dialogueState.IndicatorEntity.Set(new SpriteInfo
        {
            SpriteSheet = indicatorTexture,
            Source = new Rectangle(0, 0, indicatorTexture.Width, indicatorTexture.Height),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = _layers.GetDepth(GameDrawLayer.DialogueUI, +0.01f)
        });
        _dialogueState.IndicatorEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.UI
        });

        // Set up Yarn runtime
        var paths = dialogueContentPaths ?? new[] { "Dialogues/hello_world" };
        _dialogueRunner = new DialogueRunner();
        _variableStorage = new InMemoryVariableStorage();
        _yarnDialogue = new Yarn.Dialogue(_variableStorage)
        {
            LogDebugMessage = msg => global::System.Diagnostics.Debug.WriteLine($"[Yarn] {msg}"),
            LogErrorMessage = msg => global::System.Diagnostics.Debug.WriteLine($"[Yarn ERROR] {msg}"),
            LineHandler = OnYarnLine,
            OptionsHandler = OnYarnOptions,
            CommandHandler = OnYarnCommand,
            NodeStartHandler = OnYarnNodeStart,
            NodeCompleteHandler = OnYarnNodeComplete,
            DialogueCompleteHandler = OnYarnDialogueComplete,
        };

        // Load all yarn programs and combine via protobuf merge
        Yarn.Program combinedProgram = null;
        foreach (var path in paths)
        {
            var yarnProgram = content.Load<YarnProgram>(path);
            _dialogueRunner.AddStringTable(yarnProgram);
            var program = yarnProgram.GetProgram();
            if (combinedProgram == null)
            {
                combinedProgram = program;
            }
            else
            {
                combinedProgram.MergeFrom(program);
            }
        }
        _yarnDialogue.SetProgram(combinedProgram);
    }

    // --- Yarn callbacks (fire synchronously during Continue()) ---

    private void OnYarnLine(Line line)
    {
        var text = _dialogueRunner.GetLocalizedTextForLine(line);

        // Parse "Speaker: text" format
        string? speaker = null;
        var displayText = text;
        var colonIndex = text.IndexOf(':');
        if (colonIndex > 0 && colonIndex < 30)
        {
            speaker = text[..colonIndex].Trim();
            displayText = text[(colonIndex + 1)..].Trim();
        }

        _dialogueState.CurrentPhase = DialoguePhase.Line;
        _dialogueState.CurrentSpeaker = speaker;
        _dialogueState.WaitingForInput = false;

        // Set text content and reset reveal animation
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicText>();
        dynamicText.TextContent = displayText;
        dynamicText.VisibleCharacterCount = 0;
        dynamicText.IsRevealed = false;
        dynamicText.RevealStartTime = float.NaN;

        HideIndicator();
        HideOptions();
    }

    private void OnYarnOptions(OptionSet optionSet)
    {
        _dialogueState.CurrentPhase = DialoguePhase.Options;
        _dialogueState.CurrentOptions.Clear();
        _dialogueState.CurrentOptionIDs.Clear();
        _dialogueState.SelectedOptionIndex = 0;
        _dialogueState.WaitingForInput = true;

        foreach (var option in optionSet.Options)
        {
            if (!option.IsAvailable) continue;
            var text = _dialogueRunner.GetLocalizedTextForLine(option.Line);
            _dialogueState.CurrentOptions.Add(text);
            _dialogueState.CurrentOptionIDs.Add(option.ID);
        }

        // Clear the main text line
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicText>();
        dynamicText.TextContent = "";
        dynamicText.VisibleCharacterCount = 0;

        ShowOptions();
        HideIndicator();
    }

    private void OnYarnCommand(Command command)
    {
        global::System.Diagnostics.Debug.WriteLine($"[Yarn Command] {command.Text}");
    }

    private void OnYarnNodeStart(string nodeName)
    {
        global::System.Diagnostics.Debug.WriteLine($"[Yarn] Node started: {nodeName}");
    }

    private void OnYarnNodeComplete(string nodeName)
    {
        global::System.Diagnostics.Debug.WriteLine($"[Yarn] Node complete: {nodeName}");
    }

    private void OnYarnDialogueComplete()
    {
        _dialogueState.CurrentPhase = DialoguePhase.Complete;
        DeactivateDialogue();
    }

    // --- Dialogue start trigger (from NPCInteractionSystem) ---

    [Subscribe]
    private void OnDialogueStart(in DialogueStartMessage message)
    {
        if (_dialogueState.IsActive) return;
        StartYarnDialogue(message.StartNode);
    }

    // --- Collision trigger ---

    [Subscribe]
    private void OnCollision(in CollisionMessage message)
    {
        if (message.Type != CollisionType.Dialogue) return;
        if (_dialogueState.IsActive || _dialogueState.WasTriggered) return;

        var zoneEntity = message.CollidingEntity;
        var nodeName = "HelloWorld";

        if (zoneEntity.Has<DialogueZoneComponent>())
        {
            var zone = zoneEntity.Get<DialogueZoneComponent>();
            if (zone.OneTimeOnly && zone.HasBeenTriggered) return;
            nodeName = zone.YarnNodeName;
            zone.HasBeenTriggered = true;
        }

        StartYarnDialogue(nodeName);
    }

    public void StartYarnDialogue(string nodeName)
    {
        _dialogueState.IsActive = true;
        _dialogueState.WasTriggered = true;
        _dialogueState.InputConsumed = true; // Prevent immediate advance

        // Show dialogue box
        _dialogueState.BoxEntity.Set<Visible>();

        _world.Publish(new DialogueActiveMessage(true));

        // Start the yarn node — fires LineHandler or OptionsHandler synchronously
        _yarnDialogue.SetNode(nodeName);
        _yarnDialogue.Continue();
    }

    // --- Update loop ---

    public void Update(GameState state)
    {
        if (!_dialogueState.IsActive) return;

        // Edge-trigger: release the consumed flag when interact is no longer held
        if (!InputState.Interact.Pressed(state))
            _dialogueState.InputConsumed = false;

        switch (_dialogueState.CurrentPhase)
        {
            case DialoguePhase.Line:
                UpdateLinePhase(state);
                break;
            case DialoguePhase.Options:
                UpdateOptionsPhase(state);
                break;
        }
    }

    private void UpdateLinePhase(GameState state)
    {
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicText>();

        if (dynamicText.IsRevealed)
        {
            if (!_dialogueState.IndicatorEntity.Has<Visible>())
                _dialogueState.IndicatorEntity.Set<Visible>();
            _dialogueState.WaitingForInput = true;
        }
        else
        {
            HideIndicator();
        }

        if (InputState.Interact.Pressed(state) && !_dialogueState.InputConsumed)
        {
            _dialogueState.InputConsumed = true;

            if (!dynamicText.IsRevealed)
            {
                // Skip reveal — show all text immediately
                dynamicText.VisibleCharacterCount = dynamicText.TextContent?.Length ?? 0;
                dynamicText.IsRevealed = true;
            }
            else
            {
                // Advance to next yarn content
                _yarnDialogue.Continue();
            }
        }
    }

    private void UpdateOptionsPhase(GameState state)
    {
        if (InputState.Up.Pressed(state) && !_dialogueState.InputConsumed)
        {
            _dialogueState.InputConsumed = true;
            _dialogueState.SelectedOptionIndex =
                Math.Max(0, _dialogueState.SelectedOptionIndex - 1);
            UpdateOptionHighlights();
        }
        else if (InputState.Down.Pressed(state) && !_dialogueState.InputConsumed)
        {
            _dialogueState.InputConsumed = true;
            _dialogueState.SelectedOptionIndex =
                Math.Min(_dialogueState.CurrentOptions.Count - 1, _dialogueState.SelectedOptionIndex + 1);
            UpdateOptionHighlights();
        }

        // Release consumed flag when no navigation or interact keys are held
        if (!InputState.Up.Pressed(state) && !InputState.Down.Pressed(state) && !InputState.Interact.Pressed(state))
            _dialogueState.InputConsumed = false;

        if (InputState.Interact.Pressed(state) && !_dialogueState.InputConsumed)
        {
            _dialogueState.InputConsumed = true;
            var yarnOptionID = _dialogueState.CurrentOptionIDs[_dialogueState.SelectedOptionIndex];
            HideOptions();
            _yarnDialogue.SetSelectedOption(yarnOptionID);
            _yarnDialogue.Continue();
        }
    }

    // --- Option entity management ---

    private void ShowOptions()
    {
        HideOptions();

        const float startY = 16f;
        const float optionSpacing = 24f;

        for (var i = 0; i < _dialogueState.CurrentOptions.Count; i++)
        {
            var prefix = i == _dialogueState.SelectedOptionIndex ? "> " : "  ";
            var fullText = prefix + _dialogueState.CurrentOptions[i];

            var optionEntity = _world.CreateEntity();
            optionEntity.Set(new EntityInfo(nameof(EntityType.Interface), $"DialogueOption{i}"));
            optionEntity.Set(new Transform(new Vector2(16, startY + i * optionSpacing)));
            optionEntity.SetParent(_rootEntity);
            optionEntity.Set(new DynamicText
            {
                Target = RenderTargetID.UI,
                LayerDepth = _layers.GetDepth(GameDrawLayer.DialogueUI, +0.01f),
                Font = _font,
                Color = i == _dialogueState.SelectedOptionIndex ? Color.White : Color.SaddleBrown,
                Scale = 0.5f,
                RevealingSpeed = 0, // Instant reveal
                RevealStartTime = 0,
                IsRevealed = true,
                VisibleCharacterCount = fullText.Length,
                TextContent = fullText
            });
            optionEntity.Set(new DrawComponent
            {
                Type = DrawElementType.Text,
                Target = RenderTargetID.UI
            });
            optionEntity.Set<Visible>();

            _dialogueState.OptionEntities.Add(optionEntity);
        }
    }

    private void UpdateOptionHighlights()
    {
        for (var i = 0; i < _dialogueState.OptionEntities.Count; i++)
        {
            var entity = _dialogueState.OptionEntities[i];
            if (!entity.IsAlive) continue;

            ref var dt = ref entity.Get<DynamicText>();
            var prefix = i == _dialogueState.SelectedOptionIndex ? "> " : "  ";
            dt.TextContent = prefix + _dialogueState.CurrentOptions[i];
            dt.VisibleCharacterCount = dt.TextContent.Length;
            dt.Color = i == _dialogueState.SelectedOptionIndex ? Color.White : Color.SaddleBrown;
        }
    }

    private void HideOptions()
    {
        foreach (var entity in _dialogueState.OptionEntities)
        {
            if (entity.IsAlive) entity.Dispose();
        }
        _dialogueState.OptionEntities.Clear();
    }

    private void HideIndicator()
    {
        if (_dialogueState.IndicatorEntity.Has<Visible>())
            _dialogueState.IndicatorEntity.Remove<Visible>();
    }

    // --- Deactivation ---

    private void DeactivateDialogue()
    {
        _dialogueState.IsActive = false;
        _dialogueState.WasTriggered = false;
        _dialogueState.CurrentPhase = DialoguePhase.None;
        _dialogueState.WaitingForInput = false;
        _dialogueState.CurrentSpeaker = null;

        HideOptions();

        // Hide box
        if (_dialogueState.BoxEntity.Has<Visible>())
            _dialogueState.BoxEntity.Remove<Visible>();

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
        HideIndicator();
        var indicatorDraw = _dialogueState.IndicatorEntity.Get<DrawComponent>();
        indicatorDraw.Texture = null;

        _world.Publish(new DialogueActiveMessage(false));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
