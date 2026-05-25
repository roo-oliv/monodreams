using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Extension;
using MonoDreams.Input;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;
using Yarn;

namespace MonoDreams.Dialogue;

public class DialogueSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly Entity _rootEntity;
    private readonly DialogueStateComponent _dialogueState;
    private readonly BitmapFont _font;
    private readonly TransformComponent _rootTransform;
    private readonly float _layerDepth;
    private readonly string _entityInfoType;
    private readonly AInputState _interact;
    private readonly AInputState _up;
    private readonly AInputState _down;

    // Yarn runtime
    private readonly Yarn.Dialogue _yarnDialogue;
    private readonly DialogueRunner _dialogueRunner;
    private readonly InMemoryVariableStorage _variableStorage;

    public bool IsEnabled { get; set; } = true;

    public DialogueSystem(
        World world,
        Texture2D dialogBoxTexture,
        BitmapFont font,
        Texture2D indicatorTexture,
        int virtualWidth,
        int virtualHeight,
        float layerDepth,
        AInputState interact,
        AInputState up,
        AInputState down,
        YarnProgram[] yarnPrograms,
        string entityInfoType = "Dialogue")
    {
        _world = world;
        _font = font;
        _layerDepth = layerDepth;
        _entityInfoType = entityInfoType;
        _interact = interact;
        _up = up;
        _down = down;
        world.Subscribe(this);

        var overlayDepth = layerDepth + 0.01f;

        // Layout constants (UI coordinates, virtual resolution)
        var boxWidth = virtualWidth - 40;
        var boxHeight = 120;
        var rootPosition = new Vector2(20, virtualHeight - boxHeight - 20);
        var textOffset = new Vector2(16, 16);
        const int indicatorSize = 32;
        var indicatorOffset = new Vector2(boxWidth - indicatorSize - 12, boxHeight - indicatorSize - 8);

        // Create root entity
        _rootTransform = new TransformComponent(rootPosition);
        _rootEntity = world.CreateEntity();
        _rootEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueRoot"));
        _rootEntity.Set(_rootTransform);
        _dialogueState = new DialogueStateComponent();
        _rootEntity.Set(_dialogueState);

        // Create box child entity
        _dialogueState.BoxEntity = world.CreateEntity();
        _dialogueState.BoxEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueBox"));
        _dialogueState.BoxEntity.Set(new TransformComponent());
        _dialogueState.BoxEntity.SetParent(_rootEntity);
        _dialogueState.BoxEntity.Set(new SpriteInfoComponent
        {
            SpriteSheet = dialogBoxTexture,
            Source = new Rectangle(0, 0, dialogBoxTexture.Width, dialogBoxTexture.Height),
            Size = new Vector2(boxWidth, boxHeight),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = _layerDepth,
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
        _dialogueState.TextEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueText"));
        _dialogueState.TextEntity.Set(new TransformComponent(textOffset));
        _dialogueState.TextEntity.SetParent(_rootEntity);
        _dialogueState.TextEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.UI,
            LayerDepth = overlayDepth,
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
        _dialogueState.IndicatorEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueIndicator"));
        _dialogueState.IndicatorEntity.Set(new TransformComponent(indicatorOffset));
        _dialogueState.IndicatorEntity.SetParent(_rootEntity);
        _dialogueState.IndicatorEntity.Set(new SpriteInfoComponent
        {
            SpriteSheet = indicatorTexture,
            Source = new Rectangle(0, 0, indicatorTexture.Width, indicatorTexture.Height),
            Size = new Vector2(indicatorSize, indicatorSize),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = overlayDepth
        });
        _dialogueState.IndicatorEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.UI
        });

        // Set up Yarn runtime
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

        // Combine all yarn programs via protobuf merge
        Yarn.Program combinedProgram = null;
        foreach (var yarnProgram in yarnPrograms)
        {
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
        string speaker = null;
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
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicTextComponent>();
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
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicTextComponent>();
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

    // --- Dialogue start trigger ---

    [Subscribe]
    private void OnDialogueStart(in DialogueStartMessage message)
    {
        if (_dialogueState.IsActive) return;
        StartYarnDialogue(message.StartNode);
    }

    public void StartYarnDialogue(string nodeName)
    {
        _dialogueState.IsActive = true;
        _dialogueState.WasTriggered = true;

        // The Interact press that opened this dialogue may still be held this same
        // frame. Consume the edge so DialogueSystem.Update (later in this tick)
        // doesn't read JustPressed and advance the first line on its own opening.
        _interact.Consume();

        // Show dialogue box
        _dialogueState.BoxEntity.Set<VisibleComponent>();

        _world.Publish(new DialogueActiveMessage(true));

        // Start the yarn node — fires LineHandler or OptionsHandler synchronously
        _yarnDialogue.SetNode(nodeName);
        _yarnDialogue.Continue();
    }

    // --- Update loop ---

    public void Update(GameState state)
    {
        if (!_dialogueState.IsActive) return;

        switch (_dialogueState.CurrentPhase)
        {
            case DialoguePhase.Line:
                UpdateLinePhase(_interact.JustPressed());
                break;
            case DialoguePhase.Options:
                UpdateOptionsPhase(
                    _up.JustPressed(),
                    _down.JustPressed(),
                    _interact.JustPressed());
                break;
        }
    }

    private void UpdateLinePhase(bool interactJustPressed)
    {
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicTextComponent>();

        if (dynamicText.IsRevealed)
        {
            if (!_dialogueState.IndicatorEntity.Has<VisibleComponent>())
                _dialogueState.IndicatorEntity.Set<VisibleComponent>();
            _dialogueState.WaitingForInput = true;
        }
        else
        {
            HideIndicator();
        }

        if (!interactJustPressed) return;

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

    private void UpdateOptionsPhase(bool upJustPressed, bool downJustPressed, bool interactJustPressed)
    {
        if (upJustPressed)
        {
            _dialogueState.SelectedOptionIndex =
                Math.Max(0, _dialogueState.SelectedOptionIndex - 1);
            UpdateOptionHighlights();
        }
        else if (downJustPressed)
        {
            _dialogueState.SelectedOptionIndex =
                Math.Min(_dialogueState.CurrentOptions.Count - 1, _dialogueState.SelectedOptionIndex + 1);
            UpdateOptionHighlights();
        }

        if (interactJustPressed)
        {
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
        var overlayDepth = _layerDepth + 0.01f;

        for (var i = 0; i < _dialogueState.CurrentOptions.Count; i++)
        {
            var prefix = i == _dialogueState.SelectedOptionIndex ? "> " : "  ";
            var fullText = prefix + _dialogueState.CurrentOptions[i];

            var optionEntity = _world.CreateEntity();
            optionEntity.Set(new EntityInfoComponent(_entityInfoType, $"DialogueOption{i}"));
            optionEntity.Set(new TransformComponent(new Vector2(16, startY + i * optionSpacing)));
            optionEntity.SetParent(_rootEntity);
            optionEntity.Set(new DynamicTextComponent
            {
                Target = RenderTargetID.UI,
                LayerDepth = overlayDepth,
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
            optionEntity.Set<VisibleComponent>();

            _dialogueState.OptionEntities.Add(optionEntity);
        }
    }

    private void UpdateOptionHighlights()
    {
        for (var i = 0; i < _dialogueState.OptionEntities.Count; i++)
        {
            var entity = _dialogueState.OptionEntities[i];
            if (!entity.IsAlive) continue;

            ref var dt = ref entity.Get<DynamicTextComponent>();
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
        if (_dialogueState.IndicatorEntity.Has<VisibleComponent>())
            _dialogueState.IndicatorEntity.Remove<VisibleComponent>();

        // UI render target always renders regardless of VisibleComponent — clear the texture
        // so MasterRenderSystem skips drawing the stale DrawComponent.
        ref var indicatorDraw = ref _dialogueState.IndicatorEntity.Get<DrawComponent>();
        indicatorDraw.Texture = null;
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
        if (_dialogueState.BoxEntity.Has<VisibleComponent>())
            _dialogueState.BoxEntity.Remove<VisibleComponent>();

        var boxDraw = _dialogueState.BoxEntity.Get<DrawComponent>();
        boxDraw.Texture = null;

        // Clear text
        ref var dynamicText = ref _dialogueState.TextEntity.Get<DynamicTextComponent>();
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
