using System;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
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

    // Cursor entity set, for mouse hover/click selection of options (issue 5). Cached once (the
    // standing AsSet() rule), queried live each frame — no cursor entity ⇒ keyboard-only.
    private readonly EntitySet _cursors;

    // Set when a Yarn <<command>> fires mid-conversation. The post-command Continue() is
    // deferred to the next Update (see Update) rather than called re-entrantly inside the
    // Yarn command handler, which would risk faulting the VM.
    private bool _pendingContinue;

    // Text layout (computed in the constructor; in anchored mode recomputed each time the balloon
    // is resized to fit the final wrapped text — see ResizeAnchoredBalloon).
    private readonly float _textScale;
    private Vector2 _textLocalPos; // UI-local top-left where the line text + options start
    private float _textAreaWidth;
    private readonly float _overlayDepth;
    private readonly float _indicatorSize;

    // Option selection arrow (issue 4): a small right-pointing mesh triangle that marks the
    // currently selected option, living in a reserved left "arrow gutter" so every option's text
    // starts at the same x (no per-option prefix shift). It carries VisibleComponent permanently
    // and is shown by filling its mesh / hidden by emptying it — the same empty-to-hide rule the
    // indicator/options use, because dialogue lives on always-rendering UI or carries the tag on Main.
    private readonly Entity _optionArrowEntity;
    private readonly MeshData _optionArrowMesh;
    private readonly float _optionArrowGutter; // left indent reserved for the arrow (UI units)
    private readonly float _optionArrowSize;   // arrow triangle size (UI units)

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
    private MeshData _boxMesh;
    private readonly MeshData _balloonMesh;
    private readonly MeshData _indicatorMesh;
    private readonly Color _lineColor;
    private readonly Color _optionSelectedColor;

    // Anchored (world-space) mode (optional): instead of a fixed bottom-of-screen UI panel, the
    // whole dialogue floats above _anchorEntity on _renderTarget (Main) as a compact tailed
    // speech balloon, repositioned each frame to the anchor's world position. Mesh-mode only.
    // The balloon is borderless with subtly-rounded corners, sized dynamically to the FINAL
    // wrapped text (≤ _maxBalloonWidth), and kept inside an optional view/safe-area rectangle.
    private readonly RenderTargetID _renderTarget;
    private readonly bool _anchored;
    private readonly Entity _anchorEntity;
    private readonly Vector2 _anchorOffset;
    private float _boxWidth;
    private float _boxHeight;
    private float _tailHeight;

    // Anchored balloon chrome colour + sizing inputs (used to rebuild the mesh when the content,
    // tail direction, or tail-x changes). _maxBalloonWidth is the MAX content+padding width; the
    // actual width shrinks to the longest wrapped line. _balloonPadding is the inner text inset.
    private readonly Color _anchorFill;
    private readonly float _maxBalloonWidth;
    private readonly float _anchorPadding;

    // Optional provider of the WORLD-space rectangle the balloon must stay fully inside (a safe
    // area). Null ⇒ today's always-above, centred behaviour with no clamping. Kept as a Func so
    // the dialogue module stays decoupled from Camera — a screen passes e.g. an inset view rect.
    private readonly Func<Rectangle>? _anchorViewBounds;

    // Cached placement parameters so the balloon mesh is rebuilt only when one of them changes
    // (size / tail direction / tail attach-x), not every frame. _tailUp = tail points UP (balloon
    // below the head); _tailAttachX is the tail apex x in the box's LOCAL space.
    private bool _tailUp;
    private float _tailAttachX;
    private Vector2 _lastMeshSize = new(float.NaN, float.NaN);

    // Downward speech-balloon tail height (anchored mode) — added beyond the box, pointing at the head.
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
        // In anchored mode this is the MAX content+padding width — the balloon shrinks to the
        // longest wrapped line but never exceeds it (in non-anchored mode it overrides the box
        // width as before). Optional view/safe-area provider keeps the balloon inside a world rect.
        float? boxWidthOverride = null,
        Func<Rectangle>? anchorViewBounds = null)
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
        _anchorViewBounds = anchorViewBounds;
        _indicatorSize = indicatorSize;
        world.Subscribe(this);

        // Cursor set for mouse-driven option selection (issue 5). Cached once per the standing
        // AsSet() rule; an empty set (no cursor entity) just means keyboard-only.
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();

        var overlayDepth = layerDepth + 0.01f;
        _overlayDepth = overlayDepth;

        // Layout constants (UI coordinates, virtual resolution). By default the box fills the
        // screen width minus a margin and sits at the bottom; anchored balloons pass a compact
        // boxWidthOverride (= MAX content width) and are sized to their text + repositioned above
        // (or below) their anchor each frame (see ResizeAnchoredBalloon / RepositionAnchor).
        const float boxMargin = 20f;
        // In anchored mode boxWidthOverride caps the balloon; start AT the max so the first
        // line/options resize down to fit. Otherwise it (optionally) overrides the box width.
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
        _maxBalloonWidth = boxWidth;
        _anchorFill = chromeFill ?? Color.White;
        // Inner text inset for the anchored balloon (shares the balloon-padding knob).
        _anchorPadding = balloonPadding;
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
        else if (_anchored)
        {
            // Anchored balloon: text inset by the balloon padding all round; these are recomputed
            // by ApplyAnchoredLayout each time the balloon is resized to its final text.
            (textLocal, _textAreaWidth, indicatorOffset) = AnchoredLayout(boxWidth, boxHeight);
            PortraitGutterBounds = Rectangle.Empty;
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

        // Option selection arrow (issue 4). Size it to the option line height so it reads at any
        // textScale, and reserve a left gutter wide enough to clear the arrow plus a little air —
        // every option's text is then indented past the gutter so all options share one left x.
        _optionArrowSize = _font.LineHeight * _textScale * 0.6f;
        _optionArrowGutter = _optionArrowSize + 6f;
        // Right-pointing triangle in the arrow's local space (origin at the gutter's top-left).
        _optionArrowMesh = RightArrowMesh(_optionArrowSize, _optionSelectedColor);

        // Pre-build the chrome meshes in mesh mode (authored in each entity's local space; the
        // entity transform/parent places them). Applied on activate, emptied on deactivate.
        if (_meshMode)
        {
            var fill = chromeFill!.Value;
            var outline = chromeOutline ?? Color.White;
            if (_anchored)
            {
                // Borderless rounded balloon, tail down and centred to start. Rebuilt on resize
                // (final text) and whenever placement flips the tail or shifts the box (see
                // RebuildBalloonMeshIfNeeded). No outline — the balloon draws fill only.
                _tailUp = false;
                _tailAttachX = boxWidth / 2f;
                _boxMesh = BuildBalloonMesh();
            }
            else
            {
                _boxMesh = PanelMesh(new Rectangle(0, 0, (int)boxWidth, (int)boxHeight), fill, outline, chromeThickness);
            }
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
            // Explicit so the spoken line's wrapped multi-line leading is pinned to the engine
            // default (1.15) rather than relying on the ≤ 0 fallback; ShowOptions stacks options
            // by this same constant, keeping render advance and hand-rolled stacking in sync.
            LineSpacing = DynamicTextComponent.DefaultLineSpacing,
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

        // Create option-selection arrow child entity (issue 4): a mesh triangle in the option
        // arrow gutter, repositioned to the selected option each frame the selection changes.
        // Like the indicator/box meshes it keeps VisibleComponent permanently (so MeshPrepSystem
        // refreshes its matrix) and is shown by filling its mesh / hidden by emptying it. The text
        // path needs no separate arrow — it is mesh-only chrome and renders fine on UI or Main.
        _optionArrowEntity = world.CreateEntity();
        _optionArrowEntity.Set(new EntityInfoComponent(_entityInfoType, "DialogueOptionArrow"));
        _optionArrowEntity.Set(new TransformComponent(_textLocalPos));
        _optionArrowEntity.SetParent(_rootEntity);
        _optionArrowEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = _renderTarget,
            LayerDepth = overlayDepth,
        });
        _optionArrowEntity.Set<VisibleComponent>();

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
        // Anchored balloons size to the FINAL wrapped text first (so the balloon does NOT resize
        // as characters reveal), wrapping to the max content width; everything else wraps to the
        // current fixed text area.
        if (_anchored)
            displayText = SizeAnchoredBalloonToLine(displayText);
        else
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

        // Anchored balloons size to the full options block first, so ShowOptions lays the options
        // out inside the already-resized balloon (positions are relative to _textLocalPos).
        if (_anchored) SizeAnchoredBalloonToOptions();

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

        _world.Publish(new DialogueActiveMessage(true));

        // Start the yarn node — fires LineHandler or OptionsHandler synchronously, which (in
        // anchored mode) sizes the balloon to the first line/options.
        _yarnDialogue.SetNode(nodeName);
        _yarnDialogue.Continue();

        // Anchored mode: now that the balloon is sized to its content, place it over the anchor so
        // it appears in the right spot on the first frame (Update re-runs this each frame to track).
        RepositionAnchor();
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

        // Mouse selection (issue 5): hover sets the selection (mirroring up/down), a left-button
        // release on the hovered option confirms it (mirroring interact). Keyboard is untouched.
        var mouseConfirm = HandleOptionMouse();

        if (interactJustPressed || mouseConfirm)
            ConfirmSelectedOption();
    }

    /// Hover + click selection of options by mouse. Returns true when the cursor confirmed an
    /// option this frame (left button released while hovering). The cursor entity is queried from
    /// the world (mirroring DemoButtonInteractionSystem); with no cursor entity this is a no-op so
    /// keyboard still works. Bounds are computed from each option's LIVE world position + measured
    /// wrapped-text size, so they survive any later dynamic repositioning of the options.
    private bool HandleOptionMouse()
    {
        var cursors = _cursors.GetEntities();
        if (cursors.Length == 0) return false;
        ref readonly var cursor = ref cursors[0].Get<CursorInputComponent>();

        // Pick the coordinate space this instance renders in: Main reads world coords (camera-
        // transformed), UI/HUD read the virtual-screen coords (no camera). On Main, WorldPosition
        // is one frame stale (CursorPositionSystem runs after dialogue) — acceptable for hover/click.
        var cursorPos = _renderTarget == RenderTargetID.Main
            ? cursor.WorldPosition
            : cursor.VirtualPosition;

        var lineHeight = _font.LineHeight * _textScale * DynamicTextComponent.DefaultLineSpacing;
        for (var i = 0; i < _dialogueState.OptionEntities.Count; i++)
        {
            var option = _dialogueState.OptionEntities[i];
            if (!option.IsAlive || !option.Has<DynamicTextComponent>()) continue;

            // World-space rect from the option's live position + its wrapped text extent. Width is
            // the measured wrapped string; height uses the layout's line height per wrapped line so
            // multi-line options have a hit area matching their on-screen footprint.
            var worldPos = option.Get<TransformComponent>().WorldPosition;
            ref readonly var dt = ref option.Get<DynamicTextComponent>();
            var text = dt.TextContent ?? string.Empty;
            var width = _font.MeasureString(text).Width * _textScale;
            var height = CountLines(text) * lineHeight;
            var bounds = new Rectangle(
                (int)worldPos.X, (int)worldPos.Y, (int)width, (int)height);

            if (!bounds.Contains(cursorPos)) continue;

            // Hover updates the selection (same path as up/down), only when it actually changed so
            // we don't rebuild highlights/arrow every frame the cursor rests on an option.
            if (_dialogueState.SelectedOptionIndex != i)
            {
                _dialogueState.SelectedOptionIndex = i;
                UpdateOptionHighlights();
            }

            return cursor.LeftButtonReleased;
        }

        return false;
    }

    /// Confirms the currently selected option — the shared keyboard/mouse path. Mirrors the
    /// original keyboard interact: hide the options, tell Yarn, advance the conversation.
    private void ConfirmSelectedOption()
    {
        if (_dialogueState.CurrentOptionIDs.Count == 0) return;
        var index = Math.Clamp(_dialogueState.SelectedOptionIndex, 0, _dialogueState.CurrentOptionIDs.Count - 1);
        var yarnOptionID = _dialogueState.CurrentOptionIDs[index];
        HideOptions();
        _yarnDialogue.SetSelectedOption(yarnOptionID);
        _yarnDialogue.Continue();
    }

    // --- Option entity management ---

    private void ShowOptions()
    {
        HideOptions();

        // Match the multi-line render advance (Font.LineHeight * scale * leading) so wrapped
        // options stack without overlap — see DynamicTextComponent.DefaultLineSpacing.
        var lineHeight = _font.LineHeight * _textScale * DynamicTextComponent.DefaultLineSpacing;
        var gap = lineHeight * 0.4f;
        // Indent every option past the arrow gutter so they share one left x and leave room for
        // the selection arrow (issue 4) — no per-option prefix, all options align.
        var x = _textLocalPos.X + _optionArrowGutter;
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

        // Place + show the selection arrow against the now-built options.
        ShowOptionArrow();
    }

    /// Fills the arrow mesh and parks it beside the selected option's first line. Reads each
    /// option entity's LIVE local position so it stays correct if a later change repositions them.
    private void ShowOptionArrow()
    {
        if (_dialogueState.OptionEntities.Count == 0)
        {
            EmptyMesh(_optionArrowEntity);
            return;
        }

        var i = Math.Clamp(_dialogueState.SelectedOptionIndex, 0, _dialogueState.OptionEntities.Count - 1);
        var option = _dialogueState.OptionEntities[i];
        if (!option.IsAlive)
        {
            EmptyMesh(_optionArrowEntity);
            return;
        }

        // Options and the arrow are both children of the root, so an option's local Position is
        // directly usable to place the arrow in the same (root-local) space. Park the arrow in the
        // gutter to the option's left, vertically centred on the option's first text line.
        var optionLocal = option.Get<TransformComponent>().Position;
        var lineHeight = _font.LineHeight * _textScale * DynamicTextComponent.DefaultLineSpacing;
        var arrowX = optionLocal.X - _optionArrowGutter;
        var arrowY = optionLocal.Y + (lineHeight - _optionArrowSize) / 2f;
        _optionArrowEntity.Get<TransformComponent>().Position = new Vector2(arrowX, arrowY);
        _optionArrowEntity.Get<DrawComponent>().SetMeshData(_optionArrowMesh);
    }

    private void UpdateOptionHighlights()
    {
        for (var i = 0; i < _dialogueState.OptionEntities.Count; i++)
        {
            var entity = _dialogueState.OptionEntities[i];
            if (!entity.IsAlive) continue;

            // Selection is now signalled by colour + the moving arrow (issue 4) — the option text
            // itself never changes, so positions are stable across selection moves and the text is
            // not re-wrapped here.
            ref var dt = ref entity.Get<DynamicTextComponent>();
            dt.Color = i == _dialogueState.SelectedOptionIndex ? _optionSelectedColor : _lineColor;
        }

        // Move the arrow to the newly selected option (and re-fill it if it was empty).
        ShowOptionArrow();
    }

    /// The wrapped text for option <paramref name="i"/>. No selection prefix — every option starts
    /// at the same x past the arrow gutter, so wrap to the gutter-reduced text width.
    private string OptionDisplay(int i)
        => WrapText(_dialogueState.CurrentOptions[i], _textAreaWidth - _optionArrowGutter);

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

        // Empty the arrow mesh so it disappears with the options (it keeps VisibleComponent, so
        // emptying — not removing the tag — is how it hides; see the indicator/box meshes).
        EmptyMesh(_optionArrowEntity);
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

    /// In anchored mode, place the balloon over the anchor entity and keep it inside the optional
    /// view/safe-area bounds. Prefers ABOVE the head (tail down); flips BELOW (tail up) when the
    /// top would fall outside the bounds. Horizontally centres over the anchor, then clamps left/
    /// right to the bounds. The tail apex always tracks the anchor head's x; when the box shifts
    /// off-centre the tail attach-x is re-derived and the mesh rebuilt only on a real change.
    /// Mutating the root transform marks the child chain dirty; HierarchySystem (later in the
    /// pipeline) re-lays the box/text/indicator. No-op when not anchored or the anchor is gone.
    private void RepositionAnchor()
    {
        if (!_anchored || !_anchorEntity.IsAlive) return;

        // The tail tip target — the anchor head — in world space.
        var head = _anchorEntity.Get<TransformComponent>().WorldPosition + _anchorOffset;

        // Centre the box horizontally over the head; box top-left X.
        var x = head.X - _boxWidth / 2f;
        // Default placement: ABOVE the head, tail pointing down.
        var tailUp = false;
        var y = head.Y - _tailHeight - _boxHeight;

        if (_anchorViewBounds is { } provider)
        {
            const float margin = 6f;
            var view = provider();

            // Flip below the head (tail up) if the above-placement top falls outside the view top.
            if (y < view.Top + margin)
            {
                tailUp = true;
                y = head.Y + _tailHeight;
                // If below also overflows the bottom, keep whichever fits better (prefer above).
                if (y + _boxHeight > view.Bottom - margin && head.Y - _tailHeight - _boxHeight >= view.Top + margin)
                {
                    tailUp = false;
                    y = head.Y - _tailHeight - _boxHeight;
                }
            }

            // Horizontal clamp into [Left+margin, Right-margin] (only if the box fits).
            var minX = view.Left + margin;
            var maxX = view.Right - margin - _boxWidth;
            if (maxX >= minX) x = MathHelper.Clamp(x, minX, maxX);
        }

        _rootTransform.Position = new Vector2(x, y);

        // Tail attach-x is the head's x in the box's LOCAL space (clamped to the box span so the
        // apex stays attached to the body). Rebuild the mesh only when placement params change.
        var attachX = MathHelper.Clamp(head.X - x, 8f, _boxWidth - 8f);
        RebuildBalloonMeshIfNeeded(tailUp, attachX);
    }

    // --- anchored balloon sizing ---

    /// Wraps the spoken line to the max content width, shrinks the balloon to the longest wrapped
    /// line + padding (≤ max) and the wrapped line count, rebuilds the chrome + inner layout, then
    /// returns the wrapped text (sized to the FINAL string, so the reveal doesn't resize).
    private string SizeAnchoredBalloonToLine(string text)
    {
        var maxTextWidth = _maxBalloonWidth - 2f * _anchorPadding;
        var wrapped = WrapText(text, maxTextWidth);
        var lineHeight = _font.LineHeight * _textScale * DynamicTextComponent.DefaultLineSpacing;
        var contentWidth = MeasureLongestLine(wrapped);
        var contentHeight = CountLines(wrapped) * lineHeight;
        ResizeAnchoredBalloon(contentWidth, contentHeight);
        return wrapped;
    }

    /// Mirrors ShowOptions' measurement to size the balloon to the full options block: each option
    /// wrapped to the (gutter-reduced) max text width, stacked by line height + gap, plus the arrow
    /// gutter on the left. Sizes the balloon so ShowOptions then lays the options out within it.
    private void SizeAnchoredBalloonToOptions()
    {
        var lineHeight = _font.LineHeight * _textScale * DynamicTextComponent.DefaultLineSpacing;
        var gap = lineHeight * 0.4f;
        var maxTextWidth = _maxBalloonWidth - 2f * _anchorPadding;

        var widest = 0f;
        var totalHeight = 0f;
        for (var i = 0; i < _dialogueState.CurrentOptions.Count; i++)
        {
            var wrapped = WrapText(_dialogueState.CurrentOptions[i], maxTextWidth - _optionArrowGutter);
            widest = MathF.Max(widest, MeasureLongestLine(wrapped));
            totalHeight += CountLines(wrapped) * lineHeight;
            if (i < _dialogueState.CurrentOptions.Count - 1) totalHeight += gap;
        }

        ResizeAnchoredBalloon(widest + _optionArrowGutter, totalHeight);
    }

    /// Sets the balloon to fit content (width/height) + padding (clamped to the max width), rebuilds
    /// the rounded chrome, recomputes the inner text origin / wrap width / indicator offset, and
    /// re-parks the text, indicator, and option-arrow children for the new size.
    private void ResizeAnchoredBalloon(float contentWidth, float contentHeight)
    {
        var newWidth = MathHelper.Clamp(contentWidth + 2f * _anchorPadding, 2f * _anchorPadding + 1f, _maxBalloonWidth);
        // Reserve room below the text for the continue indicator.
        var newHeight = contentHeight + 2f * _anchorPadding + _indicatorSize * 0.5f;

        _boxWidth = newWidth;
        _boxHeight = newHeight;

        var (textLocal, wrapWidth, indicatorOffset) = AnchoredLayout(newWidth, newHeight);
        _textLocalPos = textLocal;
        _textAreaWidth = wrapWidth;

        // Re-park the text + indicator children for the new interior; the option arrow follows the
        // options (ShowOptions/ShowOptionArrow read live positions), so just point it at the origin.
        if (_dialogueState.TextEntity.IsAlive)
            _dialogueState.TextEntity.Get<TransformComponent>().Position = _textLocalPos;
        if (_dialogueState.IndicatorEntity.IsAlive)
            _dialogueState.IndicatorEntity.Get<TransformComponent>().Position = indicatorOffset;
        if (_optionArrowEntity.IsAlive)
            _optionArrowEntity.Get<TransformComponent>().Position = _textLocalPos;

        // Force a mesh rebuild next placement (size changed); RepositionAnchor sets the tail params.
        _lastMeshSize = new Vector2(float.NaN, float.NaN);
        _boxMesh = BuildBalloonMesh();
        if (_dialogueState.BoxEntity.IsAlive && _dialogueState.IsActive)
            _dialogueState.BoxEntity.Get<DrawComponent>().SetMeshData(_boxMesh);
    }

    /// The anchored balloon's interior layout for a given size: text top-left, wrap width, and the
    /// continue-indicator offset (bottom-right of the interior). Padding-inset all round.
    private (Vector2 textLocal, float wrapWidth, Vector2 indicatorOffset) AnchoredLayout(float width, float height)
    {
        var textLocal = new Vector2(_anchorPadding, _anchorPadding);
        var wrapWidth = width - 2f * _anchorPadding;
        var indicatorOffset = new Vector2(
            width - _anchorPadding - _indicatorSize * 0.6f,
            height - _anchorPadding - _indicatorSize * 0.5f);
        return (textLocal, wrapWidth, indicatorOffset);
    }

    /// The widest single wrapped line in rendered pixels (font measured at _textScale).
    private float MeasureLongestLine(string wrapped)
    {
        var widest = 0f;
        foreach (var line in wrapped.Split('\n'))
            widest = MathF.Max(widest, _font.MeasureString(line).Width * _textScale);
        return widest;
    }

    /// Rebuilds the balloon mesh only when the tail direction or attach-x changed since the last
    /// build (size changes reset the cache via ResizeAnchoredBalloon). Avoids a per-frame rebuild.
    private void RebuildBalloonMeshIfNeeded(bool tailUp, float tailAttachX)
    {
        var size = new Vector2(_boxWidth, _boxHeight);
        if (_lastMeshSize == size && _tailUp == tailUp && Math.Abs(_tailAttachX - tailAttachX) < 0.5f)
            return;
        _tailUp = tailUp;
        _tailAttachX = tailAttachX;
        _lastMeshSize = size;
        _boxMesh = BuildBalloonMesh();
        if (_dialogueState.BoxEntity.IsAlive && _dialogueState.IsActive)
            _dialogueState.BoxEntity.Get<DrawComponent>().SetMeshData(_boxMesh);
    }

    /// Builds the current anchored balloon mesh from the cached size + tail params.
    private MeshData BuildBalloonMesh()
        => BalloonMesh(new Rectangle(0, 0, (int)_boxWidth, (int)_boxHeight), _anchorFill, _tailHeight, _tailUp, _tailAttachX);

    // --- mesh chrome helpers ---

    /// A filled rectangle behind a thick outline — the minimalist box panel.
    private static MeshData PanelMesh(Rectangle rect, Color fill, Color outline, float thickness)
        => new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(rect, fill))
            .Add(new RectangleOutlineMeshGenerator(rect, thickness, outline))
            .Generate();

    /// A borderless, subtly-rounded speech balloon: a filled rounded-rectangle body plus a filled
    /// tail triangle (fill colour only — no outline). The tail points DOWN at the head when
    /// <paramref name="tailUp"/> is false (balloon above the head) and UP when true (balloon below).
    /// <paramref name="tailAttachX"/> is the apex x in the box's local space, so the tail tracks the
    /// head when the box is shifted off-centre by the horizontal clamp.
    private static MeshData BalloonMesh(Rectangle rect, Color fill, float tailHeight, bool tailUp, float tailAttachX)
    {
        // Small radius — subtle, ~0.12×height clamped to a sane pixel band.
        var radius = MathHelper.Clamp(rect.Height * 0.12f, 8f, 14f);

        var attach = MathHelper.Clamp(tailAttachX, rect.X + radius, rect.Right - radius);
        var half = MathHelper.Clamp(rect.Width * 0.045f, 8f, 16f);
        var edgeY = tailUp ? rect.Top : rect.Bottom;          // box edge the tail springs from
        var apexY = tailUp ? rect.Top - tailHeight : rect.Bottom + tailHeight;
        var baseL = new Vector2(MathHelper.Clamp(attach - half, rect.X + radius, rect.Right - radius), edgeY);
        var baseR = new Vector2(MathHelper.Clamp(attach + half, rect.X + radius, rect.Right - radius), edgeY);
        var apex = new Vector2(attach, apexY);

        return new CompositeMeshGenerator()
            .Add(new FilledRoundedRectangleMeshGenerator(rect, radius, fill))
            .Add(new FilledTriangleMeshGenerator(baseL, baseR, apex, fill))
            .Generate();
    }

    /// A small right-pointing "selection" arrow triangle fitted to a <paramref name="size"/>×
    /// <paramref name="size"/> box in local space (apex on the right, base on the left). Used to
    /// mark the selected option in the arrow gutter (issue 4).
    private static MeshData RightArrowMesh(float size, Color color)
        => new FilledTriangleMeshGenerator(
            new Vector2(0f, 0f),
            new Vector2(0f, size),
            new Vector2(size, size / 2f),
            color).Generate();

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
        _cursors.Dispose();
        GC.SuppressFinalize(this);
    }
}
