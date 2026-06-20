using System;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
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

    // Set when a Yarn <<command>> fires mid-conversation. The post-command Continue() is
    // deferred to the next Update (see Update) rather than called re-entrantly inside the
    // Yarn command handler, which would risk faulting the VM.
    private bool _pendingContinue;

    // Text layout (computed in the constructor, used by line + option rendering).
    private readonly float _textScale;
    private readonly Vector2 _textLocalPos; // UI-local top-left where the line text + options start
    private readonly float _textAreaWidth;
    private readonly float _overlayDepth;

    // Balloon mode (optional): an inner "talk balloon" panel that wraps the text, with a left
    // gutter reserved for a game-drawn emote/portrait frame. When off, the legacy box + symmetric
    // sideInset layout is used. Activated by passing a talkBalloonTexture (see the constructor).
    private readonly bool _balloonMode;
    private readonly Entity _balloonEntity;

    // Mesh chrome mode (optional): box / balloon / indicator are generated meshes (e.g. white
    // outline + black fill) instead of sprite nine-patches. Activated by passing chromeFill.
    // The texture path is untouched — MonoDreams.Examples still uses it. UI/HUD always render,
    // so these meshes are shown by filling their DrawComponent and hidden by emptying it.
    private readonly bool _meshMode;
    private readonly MeshData _boxMesh;
    private readonly MeshData _balloonMesh;
    private readonly MeshData _indicatorMesh;
    private readonly Color _lineColor;
    private readonly Color _optionSelectedColor;

    // Anchored (world-space) mode (optional): instead of a fixed bottom-of-screen UI panel, the
    // whole dialogue floats above _anchorEntity on _renderTarget (Main) as a compact tailed
    // speech balloon, repositioned each frame to the anchor's world position. Mesh-mode only.
    private readonly RenderTargetID _renderTarget;
    private readonly bool _anchored;
    private readonly Entity _anchorEntity;
    private readonly Vector2 _anchorOffset;
    private readonly float _boxWidth;
    private readonly float _boxHeight;
    private readonly float _tailHeight;

    // Downward speech-balloon tail height (anchored mode) — added below the box, pointing at the head.
    private const float AnchoredTailHeight = 22f;

    /// UI-space rectangle of the left portrait gutter — the box's left region reserved for a
    /// game-drawn emote frame — when balloon mode is active; <see cref="Rectangle.Empty"/> otherwise.
    public Rectangle PortraitGutterBounds { get; }

    // Yarn runtime
    private readonly Yarn.Dialogue _yarnDialogue;
    private readonly DialogueRunner _dialogueRunner;
    private readonly InMemoryVariableStorage _variableStorage;

    public bool IsEnabled { get; set; } = true;

    public DialogueSystem(
        World world,
        Texture2D? dialogBoxTexture,
        BitmapFont font,
        Texture2D? indicatorTexture,
        int virtualWidth,
        int virtualHeight,
        float layerDepth,
        AInputState interact,
        AInputState up,
        AInputState down,
        YarnProgram[] yarnPrograms,
        string entityInfoType = "Dialogue",
        float textScale = 0.32f,
        float sideInset = 0f,
        float indicatorSize = 44f,
        Texture2D? talkBalloonTexture = null,
        NinePatchInfo? talkBalloonNinePatch = null,
        float portraitGutter = 0f,
        float balloonPadding = 12f,
        float boxHeight = 120f,
        NinePatchInfo? boxNinePatch = null,
        // Mesh chrome (optional). Passing chromeFill switches box/balloon/indicator to
        // generated meshes; balloon mode then turns on whenever portraitGutter > 0.
        Color? chromeFill = null,
        Color? chromeOutline = null,
        float chromeThickness = 2f,
        Color? indicatorColor = null,
        // Anchored (world-space) presentation (optional). Passing anchorEntity floats the whole
        // dialogue above that entity on the given renderTarget (use Main), as a compact tailed
        // speech balloon that tracks the entity each frame. Requires mesh chrome (chromeFill).
        RenderTargetID renderTarget = RenderTargetID.UI,
        Entity? anchorEntity = null,
        Vector2 anchorOffset = default,
        float? boxWidthOverride = null)
    {
        _world = world;
        _font = font;
        _layerDepth = layerDepth;
        _entityInfoType = entityInfoType;
        _interact = interact;
        _up = up;
        _down = down;
        _textScale = textScale;
        _renderTarget = renderTarget;
        _anchored = anchorEntity.HasValue;
        _anchorEntity = anchorEntity ?? default;
        _anchorOffset = anchorOffset;
        world.Subscribe(this);

        var overlayDepth = layerDepth + 0.01f;
        _overlayDepth = overlayDepth;

        // Layout constants (UI coordinates, virtual resolution). By default the box fills the
        // screen width minus a margin and sits at the bottom; anchored balloons pass a compact
        // boxWidthOverride and are repositioned above their anchor each frame (see RepositionAnchor).
        const float boxMargin = 20f;
        var boxWidth = boxWidthOverride ?? (virtualWidth - 2f * boxMargin);
        var rootPosition = new Vector2(boxMargin, virtualHeight - boxHeight - boxMargin);

        _meshMode = chromeFill.HasValue;
        // Anchored mode draws a tailed speech balloon on the Main target; it relies on the mesh
        // show/hide path and never reserves a portrait gutter.
        if (_anchored && !_meshMode)
            throw new ArgumentException(
                "Anchored dialogue requires mesh chrome — pass chromeFill.", nameof(anchorEntity));
        _boxWidth = boxWidth;
        _boxHeight = boxHeight;
        _tailHeight = _anchored ? AnchoredTailHeight : 0f;
        // In mesh mode the portrait gutter (not a balloon texture) decides balloon layout; anchored
        // mode forces the legacy text-on-box layout (no inner balloon / portrait gutter).
        _balloonMode = !_anchored && (_meshMode ? portraitGutter > 0f : talkBalloonTexture != null);
        _lineColor = _meshMode ? new Color(238, 232, 213) : Color.SaddleBrown;
        _optionSelectedColor = _meshMode ? new Color(250, 218, 147) : Color.White;

        // Where the line text + options start, the text wrap width, and the continue indicator,
        // differ between balloon mode (text lives inside an inner panel, left gutter reserved for
        // a game-drawn emote frame) and legacy mode (text on the box, symmetric sideInset).
        Vector2 textLocal;
        Vector2 indicatorOffset;
        float balloonX = 0f, balloonY = 0f, balloonW = 0f, balloonH = 0f;
        if (_balloonMode)
        {
            const float vMargin = 10f;   // balloon inset from the box top/bottom
            const float rightMargin = 16f; // balloon inset from the box right edge
            balloonX = portraitGutter;
            balloonY = vMargin;
            balloonW = boxWidth - portraitGutter - rightMargin;
            balloonH = boxHeight - 2f * vMargin;
            textLocal = new Vector2(balloonX + balloonPadding, balloonY + balloonPadding);
            _textAreaWidth = balloonW - 2f * balloonPadding;
            indicatorOffset = new Vector2(
                balloonX + balloonW - indicatorSize - 10f,
                balloonY + balloonH - indicatorSize - 8f);
            // UI-space rect of the left gutter, for the demo to place its emote frame.
            PortraitGutterBounds = new Rectangle(
                (int)rootPosition.X, (int)rootPosition.Y, (int)portraitGutter, (int)boxHeight);
        }
        else
        {
            var textOffset = new Vector2(16, 16);
            // Line + option text wrap within the box minus symmetric side insets — the insets
            // reserve room on each side for a game-drawn portrait (see sideInset).
            textLocal = new Vector2(textOffset.X + sideInset, textOffset.Y);
            _textAreaWidth = boxWidth - 2f * textOffset.X - 2f * sideInset;
            // Continue marker sits at the bottom-right of the TEXT area (inset past any portrait).
            indicatorOffset = new Vector2(boxWidth - sideInset - indicatorSize - 12, boxHeight - indicatorSize - 8);
            PortraitGutterBounds = Rectangle.Empty;
        }
        _textLocalPos = textLocal;

        // Pre-build the chrome meshes in mesh mode (authored in each entity's local space; the
        // entity transform/parent places them). Applied on activate, emptied on deactivate.
        if (_meshMode)
        {
            var fill = chromeFill!.Value;
            var outline = chromeOutline ?? Color.White;
            var boxRect = new Rectangle(0, 0, (int)boxWidth, (int)boxHeight);
            _boxMesh = _anchored
                ? BalloonMesh(boxRect, fill, outline, chromeThickness, _tailHeight)
                : PanelMesh(boxRect, fill, outline, chromeThickness);
            if (_balloonMode)
                _balloonMesh = new RectangleOutlineMeshGenerator(
                    new Rectangle(0, 0, (int)balloonW, (int)balloonH), chromeThickness, outline).Generate();
            _indicatorMesh = DownCaretMesh(indicatorSize, indicatorColor ?? Color.White);
        }

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
        if (_meshMode)
        {
            // Mesh box: empty until activated; VisibleComponent kept on so MeshPrepSystem
            // always refreshes the world matrix (UI renders regardless of the tag).
            _dialogueState.BoxEntity.Set(new DrawComponent
            {
                Type = DrawElementType.Mesh,
                Target = _renderTarget,
                LayerDepth = _layerDepth,
            });
            _dialogueState.BoxEntity.Set<VisibleComponent>();
        }
        else
        {
            _dialogueState.BoxEntity.Set(new SpriteInfoComponent
            {
                SpriteSheet = dialogBoxTexture,
                Source = new Rectangle(0, 0, dialogBoxTexture!.Width, dialogBoxTexture.Height),
                Size = new Vector2(boxWidth, boxHeight),
                Color = Color.White,
                Target = _renderTarget,
                LayerDepth = _layerDepth,
                // Default nine-patch suits the 128×48 "dialog box medium" art; pass boxNinePatch to
                // back the box with a different panel texture.
                NinePatchData = boxNinePatch ?? new NinePatchInfo(
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
                Target = _renderTarget
            });
        }

        // Create inner talk-balloon child (balloon mode only): a nine-patch panel that holds the
        // text/options/indicator, drawn just above the box and beside the left portrait gutter.
        if (_balloonMode)
        {
            _balloonEntity = world.CreateEntity();
            _balloonEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueBalloon"));
            _balloonEntity.Set(new TransformComponent(new Vector2(balloonX, balloonY)));
            _balloonEntity.SetParent(_rootEntity);
            if (_meshMode)
            {
                // Inner frame: outline only, so the box's fill shows through behind the text.
                _balloonEntity.Set(new DrawComponent
                {
                    Type = DrawElementType.Mesh,
                    Target = _renderTarget,
                    LayerDepth = _layerDepth + 0.005f,
                });
                _balloonEntity.Set<VisibleComponent>();
            }
            else
            {
                _balloonEntity.Set(new SpriteInfoComponent
                {
                    SpriteSheet = talkBalloonTexture,
                    Source = new Rectangle(0, 0, talkBalloonTexture!.Width, talkBalloonTexture.Height),
                    Size = new Vector2(balloonW, balloonH),
                    Color = Color.White,
                    Target = _renderTarget,
                    LayerDepth = _layerDepth + 0.005f, // between the box and the text/options
                    NinePatchData = talkBalloonNinePatch,
                });
                _balloonEntity.Set(new DrawComponent
                {
                    Type = DrawElementType.Sprite,
                    Target = _renderTarget
                });
            }
        }

        // Create text child entity
        _dialogueState.TextEntity = world.CreateEntity();
        _dialogueState.TextEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueText"));
        _dialogueState.TextEntity.Set(new TransformComponent(_textLocalPos));
        _dialogueState.TextEntity.SetParent(_rootEntity);
        _dialogueState.TextEntity.Set(new DynamicTextComponent
        {
            Target = _renderTarget,
            LayerDepth = overlayDepth,
            Font = _font,
            Color = _lineColor,
            Scale = textScale,
            RevealingSpeed = 20,
            RevealStartTime = float.NaN,
            IsRevealed = false,
            VisibleCharacterCount = 0,
            TextContent = ""
        });
        _dialogueState.TextEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Text,
            Target = _renderTarget
        });
        // The Main target consults VisibleComponent (UI/HUD always render); anchored dialogue
        // lives on Main, so the text needs the tag to be drawn. TextPrepSystem doesn't gate on it.
        if (_renderTarget != RenderTargetID.UI)
            _dialogueState.TextEntity.Set<VisibleComponent>();

        // Create indicator child entity
        _dialogueState.IndicatorEntity = world.CreateEntity();
        _dialogueState.IndicatorEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueIndicator"));
        _dialogueState.IndicatorEntity.Set(new TransformComponent(indicatorOffset));
        _dialogueState.IndicatorEntity.SetParent(_rootEntity);
        if (_meshMode)
        {
            // Mesh caret: empty until a line is fully revealed; VisibleComponent kept on.
            _dialogueState.IndicatorEntity.Set(new DrawComponent
            {
                Type = DrawElementType.Mesh,
                Target = _renderTarget,
                LayerDepth = overlayDepth,
            });
            _dialogueState.IndicatorEntity.Set<VisibleComponent>();
        }
        else
        {
            _dialogueState.IndicatorEntity.Set(new SpriteInfoComponent
            {
                SpriteSheet = indicatorTexture,
                Source = new Rectangle(0, 0, indicatorTexture!.Width, indicatorTexture.Height),
                Size = new Vector2(indicatorSize, indicatorSize),
                Color = Color.White,
                Target = _renderTarget,
                LayerDepth = overlayDepth
            });
            _dialogueState.IndicatorEntity.Set(new DrawComponent
            {
                Type = DrawElementType.Sprite,
                Target = _renderTarget
            });
        }

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

        // Wrap so long lines break onto new lines instead of overflowing the box. The reveal
        // animation slices the wrapped string, so embedded newlines just advance instantly.
        displayText = WrapText(displayText, _textAreaWidth);

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
        // Surface the command so game code can react (emotes, SFX, flags), then flag the
        // conversation to flow past it on the next Update — see _pendingContinue.
        _world.Publish(new DialogueCommandMessage(command.Text));
        _pendingContinue = true;
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
        // Route by node ownership: with multiple DialogueSystems registered, every instance
        // receives the message — only the one whose merged Yarn program contains the node reacts.
        if (!_yarnDialogue.NodeExists(message.StartNode)) return;
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

        // Show dialogue box (and inner balloon, if any). In mesh mode fill the meshes (the
        // panels already carry VisibleComponent); in texture mode set VisibleComponent, which
        // gates SpritePrepSystem filling the nine-patch texture.
        if (_meshMode)
        {
            _dialogueState.BoxEntity.Get<DrawComponent>().SetMeshData(_boxMesh);
            if (_balloonMode) _balloonEntity.Get<DrawComponent>().SetMeshData(_balloonMesh);
        }
        else
        {
            _dialogueState.BoxEntity.Set<VisibleComponent>();
            if (_balloonMode) _balloonEntity.Set<VisibleComponent>();
        }

        // Anchored mode: place the balloon above the anchor now so it appears in the right spot
        // on the first frame (Update also re-runs this each frame to track the anchor).
        RepositionAnchor();

        _world.Publish(new DialogueActiveMessage(true));

        // Start the yarn node — fires LineHandler or OptionsHandler synchronously
        _yarnDialogue.SetNode(nodeName);
        _yarnDialogue.Continue();
    }

    // --- Update loop ---

    public void Update(GameState state)
    {
        if (!_dialogueState.IsActive) return;

        // Anchored mode: keep the balloon floating above its anchor as the entity (or camera) moves.
        RepositionAnchor();

        // A <<command>> fired during the previous Continue(). Advance past it now, outside
        // the Yarn handler, so an inline command (e.g. <<emote ...>>) doesn't stall the line.
        if (_pendingContinue)
        {
            _pendingContinue = false;
            _yarnDialogue.Continue();
            if (!_dialogueState.IsActive) return; // command was the last thing before ===
        }

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
            ShowIndicator();
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

        // Match the multi-line render advance (Font.LineHeight * scale * leading) so wrapped
        // options stack without overlap — see DynamicTextComponent.DefaultLineSpacing.
        var lineHeight = _font.LineHeight * _textScale * DynamicTextComponent.DefaultLineSpacing;
        var gap = lineHeight * 0.4f;
        var x = _textLocalPos.X;
        var y = _textLocalPos.Y;

        for (var i = 0; i < _dialogueState.CurrentOptions.Count; i++)
        {
            var fullText = OptionDisplay(i);

            var optionEntity = _world.CreateEntity();
            optionEntity.Set(new EntityInfoComponent(_entityInfoType, $"DialogueOption{i}"));
            optionEntity.Set(new TransformComponent(new Vector2(x, y)));
            optionEntity.SetParent(_rootEntity);
            optionEntity.Set(new DynamicTextComponent
            {
                Target = _renderTarget,
                LayerDepth = _overlayDepth,
                Font = _font,
                Color = i == _dialogueState.SelectedOptionIndex ? _optionSelectedColor : _lineColor,
                Scale = _textScale,
                RevealingSpeed = 0, // Instant reveal
                RevealStartTime = 0,
                IsRevealed = true,
                VisibleCharacterCount = fullText.Length,
                TextContent = fullText,
            });
            optionEntity.Set(new DrawComponent
            {
                Type = DrawElementType.Text,
                Target = _renderTarget
            });
            optionEntity.Set<VisibleComponent>();

            _dialogueState.OptionEntities.Add(optionEntity);

            // Advance by this option's wrapped height so multi-line options never overlap.
            var lines = CountLines(fullText);
            y += lines * lineHeight + gap;
        }
    }

    private void UpdateOptionHighlights()
    {
        for (var i = 0; i < _dialogueState.OptionEntities.Count; i++)
        {
            var entity = _dialogueState.OptionEntities[i];
            if (!entity.IsAlive) continue;

            ref var dt = ref entity.Get<DynamicTextComponent>();
            // The font is monospace and the "> "/"  " prefix is a fixed width, so re-wrapping
            // here can't change line count — option positions stay put across selection moves.
            dt.TextContent = OptionDisplay(i);
            dt.VisibleCharacterCount = dt.TextContent.Length;
            dt.Color = i == _dialogueState.SelectedOptionIndex ? _optionSelectedColor : _lineColor;
        }
    }

    /// The selection-prefixed, wrapped text for option <paramref name="i"/>.
    private string OptionDisplay(int i)
    {
        var prefix = i == _dialogueState.SelectedOptionIndex ? "> " : "  ";
        var prefixWidth = _font.MeasureString(prefix).Width * _textScale;
        var wrapped = WrapText(_dialogueState.CurrentOptions[i], _textAreaWidth - prefixWidth);
        return prefix + wrapped;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var n = 1;
        foreach (var c in text) if (c == '\n') n++;
        return n;
    }

    /// Greedy word-wrap to <paramref name="maxWidth"/> rendered pixels (font measured at
    /// <see cref="_textScale"/>), inserting newlines. A single word wider than maxWidth is
    /// left on its own line rather than split.
    private string WrapText(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0) return text;

        var result = new StringBuilder();
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && _font.MeasureString(candidate).Width * _textScale > maxWidth)
            {
                if (result.Length > 0) result.Append('\n');
                result.Append(line);
                line.Clear();
                line.Append(word);
            }
            else
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
        }
        if (line.Length > 0)
        {
            if (result.Length > 0) result.Append('\n');
            result.Append(line);
        }
        return result.ToString();
    }

    private void HideOptions()
    {
        foreach (var entity in _dialogueState.OptionEntities)
        {
            if (entity.IsAlive) entity.Dispose();
        }
        _dialogueState.OptionEntities.Clear();
    }

    /// Shows the "continue" caret: fills the mesh in mesh mode, or sets VisibleComponent so
    /// SpritePrepSystem refills the sprite in texture mode.
    private void ShowIndicator()
    {
        if (_meshMode)
            _dialogueState.IndicatorEntity.Get<DrawComponent>().SetMeshData(_indicatorMesh);
        else if (!_dialogueState.IndicatorEntity.Has<VisibleComponent>())
            _dialogueState.IndicatorEntity.Set<VisibleComponent>();
    }

    private void HideIndicator()
    {
        if (_meshMode)
        {
            EmptyMesh(_dialogueState.IndicatorEntity);
            return;
        }

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
        // Drop any pending command-continue so a command that coincides with the end of a
        // conversation (e.g. <<stop>>) can't leak a stray Continue() into the next dialogue.
        _pendingContinue = false;
        _dialogueState.IsActive = false;
        _dialogueState.WasTriggered = false;
        _dialogueState.CurrentPhase = DialoguePhase.None;
        _dialogueState.WaitingForInput = false;
        _dialogueState.CurrentSpeaker = null;

        HideOptions();

        // Hide box (and inner balloon): empty the meshes in mesh mode (VisibleComponent stays
        // on so MeshPrepSystem keeps updating the matrix), else drop the tag + texture.
        if (_meshMode)
        {
            EmptyMesh(_dialogueState.BoxEntity);
            if (_balloonMode) EmptyMesh(_balloonEntity);
        }
        else
        {
            if (_dialogueState.BoxEntity.Has<VisibleComponent>())
                _dialogueState.BoxEntity.Remove<VisibleComponent>();
            _dialogueState.BoxEntity.Get<DrawComponent>().Texture = null;

            if (_balloonMode)
            {
                if (_balloonEntity.Has<VisibleComponent>())
                    _balloonEntity.Remove<VisibleComponent>();
                _balloonEntity.Get<DrawComponent>().Texture = null;
            }
        }

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

    // --- anchored (world-space) mode ---

    /// In anchored mode, place the balloon above the anchor entity: centred over it horizontally
    /// and lifted so the tail tip sits at <c>anchor.WorldPosition + _anchorOffset</c>. Mutating the
    /// root transform marks the child chain dirty; HierarchySystem (later in the pipeline) re-lays
    /// the box/text/indicator. No-op when not anchored or the anchor is gone.
    private void RepositionAnchor()
    {
        if (!_anchored || !_anchorEntity.IsAlive) return;
        var anchor = _anchorEntity.Get<TransformComponent>().WorldPosition;
        _rootTransform.Position = new Vector2(
            anchor.X + _anchorOffset.X - _boxWidth / 2f,
            anchor.Y + _anchorOffset.Y - _boxHeight - _tailHeight);
    }

    // --- mesh chrome helpers ---

    /// A filled rectangle behind a thick outline — the minimalist box panel.
    private static MeshData PanelMesh(Rectangle rect, Color fill, Color outline, float thickness)
        => new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(rect, fill))
            .Add(new RectangleOutlineMeshGenerator(rect, thickness, outline))
            .Generate();

    /// A panel with a downward tail at its bottom-centre — the speech-balloon chrome used in
    /// anchored mode. The tail apex sits <paramref name="tailHeight"/> below the box bottom and
    /// points at the anchored entity's head.
    private static MeshData BalloonMesh(Rectangle rect, Color fill, Color outline, float thickness, float tailHeight)
    {
        var cx = rect.X + rect.Width / 2f;
        var by = rect.Y + rect.Height;
        var half = MathHelper.Clamp(rect.Width * 0.05f, 9f, 20f);
        var apex = new Vector2(cx, by + tailHeight);
        var left = new Vector2(cx - half, by);
        var right = new Vector2(cx + half, by);
        return new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(rect, fill))
            .Add(new FilledTriangleMeshGenerator(left, right, apex, fill))
            .Add(new RectangleOutlineMeshGenerator(rect, thickness, outline))
            .Add(new PolylineMeshGenerator(new[] { left, apex, right }, thickness, outline))
            .Generate();
    }

    /// A small downward-pointing "continue" caret triangle fitted to <paramref name="size"/>.
    private static MeshData DownCaretMesh(float size, Color color)
        => new FilledTriangleMeshGenerator(
            new Vector2(0.18f * size, 0.32f * size),
            new Vector2(0.82f * size, 0.32f * size),
            new Vector2(0.50f * size, 0.72f * size),
            color).Generate();

    /// Empties an entity's mesh so MasterRenderSystem skips it (UI/HUD always render, so this
    /// is how a mesh is hidden there). Keeps Type = Mesh and the entity's VisibleComponent.
    private static void EmptyMesh(Entity e)
    {
        if (!e.IsAlive || !e.Has<DrawComponent>()) return;
        ref var draw = ref e.Get<DrawComponent>();
        draw.Type = DrawElementType.Mesh;
        draw.Vertices = [];
        draw.Indices = [];
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
