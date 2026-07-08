using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using DefaultEcs;
using DefaultEcs.System;
using Google.Protobuf;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Demos;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Dialogue;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.System.Input;
using MonoDreams.UI;
using MonoDreams.Util;
using MonoGame.Extended.BitmapFonts;
using Yarn.Compiler;

namespace MonoDreams.Demo.Dialogue;

/// Dialogue module demo. A very basic top-down scene drawn entirely with generated meshes:
/// walk the player blob around a grass field with WASD, approach the cow NPC at the top-left,
/// and press E to start a Yarn conversation. The line text wraps and reveals
/// character-by-character; pick a reply with the up/down arrows and E. Yarn
/// <c>&lt;&lt;emote who kind&gt;&gt;</c> commands show the speaker's framed mesh portrait inside
/// the box and <c>&lt;&lt;react who mark&gt;&gt;</c> pops a mesh reaction mark above a head.
///
/// Showcases the dialogue module (<see cref="DialogueSystem"/> + Yarn runtime, fed an
/// in-memory-compiled program so the demo ships no .yarn asset) in its mesh-chrome mode, the
/// rendering-text reveal, and procedural mesh rendering on the Main/UI targets.
public class DialogueDemoScreen : IGameScreen
{
    private const float BoundaryHalfWidth = 380f;
    private const float BoundaryHalfHeight = 220f;
    private const float PlayerSpeed = 135f;       // 0.75× the original
    private const float PlayerBodyRadius = 30f;   // mesh player: body radius (feet at the transform)
    private const float CowBodyRadius = 42f;      // mesh cow: a little larger than the player
    private const float BirdBodyRadius = 26f;     // mesh bird: the smaller upper-right NPC
    private const float InteractRange = 170f;
    private const float ReactionDuration = 1.6f;  // above-head reaction marks auto-hide
    private const float ReactionMarkSize = 36f;   // above-head mesh glyph size

    // Dialogue-box tuning passed into DialogueSystem. The box is now generated meshes
    // (white outline, black fill) with a left gutter holding a framed emote glyph.
    private const float DialogueTextScale = 0.27f;
    private const float DialogueIndicatorSize = 40f;
    private const float DialogueBoxHeight = 150f;
    private const float DialoguePortraitGutter = 140f; // left reserve inside the box for the emote frame
    private const float DialogueBalloonPadding = 14f;

    // The bird's over-the-head speech balloon (anchored DialogueSystem on the Main target). A
    // compact tailed bubble that floats over the bird and tracks it. It now sizes dynamically to
    // its text (≤ BirdBalloonMaxWidth) and flips above/below the bird to stay inside a safe area
    // (the camera view inset below the top header), so the constants only cap the size + tune text.
    private const float BirdBalloonMaxWidth = 300f;     // MAX content width — the bubble stays narrow
    private const float BirdBalloonHeight = 124f;       // initial height; resized to the text
    private const float BirdBalloonTextScale = 0.26f;
    private const float BirdBalloonIndicatorSize = 34f;
    // Top header band height (world px at zoom 1) the balloon's safe area must clear (HUD overlay).
    private const float HeaderBandHeight = 115f;

    // The in-box emote portrait: a mesh frame (white outline, black fill) with the speaker's
    // mesh face glyph centred inside it.
    private const float FrameRenderSize = 116f;   // the square emote frame
    private const float FaceRenderSize = 84f;     // speaker face glyph, centred in the frame

    private static readonly Vector2 PlayerSpawn = new(40f, 90f);
    // Upper-left, but low enough that the above-head "E to talk" prompt clears the header banner.
    private static readonly Vector2 NpcPosition = new(-300f, -60f);
    // Genuine upper-RIGHT corner. There is no room above the bird for the balloon (the head is near
    // the camera top, under the header), so the anchored balloon auto-flips BELOW the bird — the
    // camera-aware placement we want to showcase. (Feet at the transform; body rises toward -Y.)
    private static readonly Vector2 BirdPosition = new(300f, -150f);

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;

    // Shared per-action input. The SAME interact instance feeds both the NPC trigger and
    // DialogueSystem, so DialogueSystem.StartYarnDialogue's _interact.Consume() stops the
    // opening E press from also advancing the first line.
    private readonly DemoInputState _interact = new();
    private readonly DemoInputState _up = new();
    private readonly DemoInputState _down = new();

    private ScreenController? _screenController;
    private Entity _player;
    private Entity _npc;
    private Entity _bird;
    private bool _dialogueActive;

    // The universal editor overlay (null when editorEnabled is false) and the retained pipeline
    // registries the editor's systems panel binds to (see DemoEditor). Bound in Load — this
    // screen builds its pipelines there (DialogueSystem needs textures from `content`).
    private readonly bool _editorEnabled;
    private readonly DrawLayerMap _layers = DemoEditor.CreateLayers();
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private DemoEditor? _editor;

    public ISystem<GameState> UpdateSystem { get; private set; } = null!;
    public ISystem<GameState> DrawSystem { get; private set; } = null!;
    public World World => _world;

    public DialogueDemoScreen(GraphicsDevice graphicsDevice, ContentManager content,
        MonoDreams.Component.Camera camera, ViewportManager viewportManager, SpriteBatch spriteBatch,
        bool editorEnabled = false)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _editorEnabled = editorEnabled;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
        };
        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        camera.Position = Vector2.Zero;

        _world = new World();
        // Pipelines are built in Load — DialogueSystem needs textures from `content`.
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnButtonClicked);
        _world.Subscribe<DialogueActiveMessage>(OnDialogueActive);

        MonoDreams.Cursor.Cursor.CreateMesh(_world,
            ShapeBuilder.Arrow(26f, Color.Black, Color.White).Generate(), RenderTargetID.HUD);

        CreateGround();
        CreateBoundary();
        var playerMark = CreatePlayer();
        var (npcMark, npcPrompt) = CreateNpc(
            out _npc, NpcPosition, DialogueGlyphs.CowShape(CowBodyRadius), "DialogueDemoCow",
            CowBodyRadius);
        var (birdMark, birdPrompt) = CreateNpc(
            out _bird, BirdPosition, DialogueGlyphs.BirdShape(BirdBodyRadius), "DialogueDemoBird",
            BirdBodyRadius);

        // Cow conversation (node "Start"): a fixed bottom-of-screen box with a left portrait
        // gutter. Box / balloon / indicator are generated meshes (white outline, black fill).
        var cowDialogue = new DialogueSystem(
            _world,
            dialogBoxTexture: null,
            _font,
            indicatorTexture: null,
            _viewportManager.VirtualWidth, _viewportManager.VirtualHeight,
            layerDepth: 0.9f,
            _interact, _up, _down,
            new[] { CompileYarn(YarnSource) },
            textScale: DialogueTextScale,
            indicatorSize: DialogueIndicatorSize,
            portraitGutter: DialoguePortraitGutter,
            balloonPadding: DialogueBalloonPadding,
            boxHeight: DialogueBoxHeight,
            chromeFill: Color.Black,
            chromeOutline: Color.White,
            chromeThickness: 2f,
            indicatorColor: Color.White);

        // Bird conversation (node "Bird"): the SAME dialogue engine in its anchored mode — a
        // borderless, rounded speech balloon on the Main target that floats over the bird and
        // tracks it. It sizes to its text (≤ BirdBalloonMaxWidth) and flips above/below the bird to
        // stay inside a safe area: the camera view with the TOP inset past the HUD header band and
        // small side/bottom margins (the header is a HUD overlay, not part of camera bounds — we
        // inset the provided rect so the balloon avoids it). No outline (the balloon draws fill
        // only); a warm dark chrome distinguishes it from the cow's box. Both DialogueSystems hear
        // every DialogueStartMessage but only react to nodes they own.
        var birdDialogue = new DialogueSystem(
            _world,
            dialogBoxTexture: null,
            _font,
            indicatorTexture: null,
            _viewportManager.VirtualWidth, _viewportManager.VirtualHeight,
            layerDepth: 0.7f,
            _interact, _up, _down,
            new[] { CompileYarn(BirdYarnSource) },
            textScale: BirdBalloonTextScale,
            indicatorSize: BirdBalloonIndicatorSize,
            balloonPadding: DialogueBalloonPadding,
            boxHeight: BirdBalloonHeight,
            chromeFill: new Color(24, 26, 34),
            chromeThickness: 2f,
            indicatorColor: new Color(250, 224, 150),
            renderTarget: RenderTargetID.Main,
            anchorEntity: _bird,
            anchorOffset: new Vector2(0f, -2.7f * BirdBodyRadius),
            boxWidthOverride: BirdBalloonMaxWidth,
            anchorViewBounds: BirdBalloonSafeArea);

        // A single emote frame in the cow box's left gutter shows whoever is speaking.
        var portrait = CreatePortraitSlot(cowDialogue.PortraitGutterBounds);

        BuildHud(content);

        // <<react who mark>> → above-head mesh mark (keyed by speaker); <<emote who kind>> → the
        // cow box's in-box portrait. Both arrive as DialogueCommandMessage from either dialogue.
        var marks = new Dictionary<string, Entity>
        {
            ["player"] = playerMark,
            ["npc"] = npcMark,
            ["bird"] = birdMark,
        };
        var reactionSystem = new ReactionMarkSystem(_world, marks, ReactionDuration, ReactionMarkSize);
        var portraitSystem = new DialoguePortraitSystem(_world, portrait, FrameRenderSize, FaceRenderSize);

        var npcTargets = new[]
        {
            new NpcInteractionTarget(_npc, npcPrompt, "Start"),
            new NpcInteractionTarget(_bird, birdPrompt, "Bird"),
        };

        UpdateSystem = CreateUpdateSystem(cowDialogue, birdDialogue, npcTargets, reactionSystem, portraitSystem);
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.Overlay.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(DialogueDemoScreen), _updatePipeline, _drawPipeline);
        }
    }

    // ─── scene ────────────────────────────────────────────────────────────────

    /// The grass field: a filled green rectangle covering the boundary, with a faint grid
    /// of lines for texture. Replaces the old grass tile sprites.
    private void CreateGround()
    {
        var field = new Rectangle(
            -(int)BoundaryHalfWidth, -(int)BoundaryHalfHeight,
            (int)BoundaryHalfWidth * 2, (int)BoundaryHalfHeight * 2);
        ShapeBuilder.Filled(_world, field, new Color(58, 92, 64), RenderTargetID.Main, depth: 0.08f);

        // Faint grid lines so the field reads as ground rather than a flat block.
        const float step = 80f;
        var grid = new CompositeMeshGenerator();
        var lineColor = new Color(72, 110, 78);
        for (var x = field.Left + (int)step; x < field.Right; x += (int)step)
            grid.Add(new LineMeshGenerator(new Vector2(x, field.Top), new Vector2(x, field.Bottom), 1f, lineColor));
        for (var y = field.Top + (int)step; y < field.Bottom; y += (int)step)
            grid.Add(new LineMeshGenerator(new Vector2(field.Left, y), new Vector2(field.Right, y), 1f, lineColor));
        ShapeBuilder.Create(_world, grid, RenderTargetID.Main, depth: 0.09f);
    }

    private void CreateBoundary()
    {
        var bounds = new Rectangle(
            -(int)BoundaryHalfWidth, -(int)BoundaryHalfHeight,
            (int)BoundaryHalfWidth * 2, (int)BoundaryHalfHeight * 2);

        var boundary = _world.CreateEntity();
        boundary.Set(new TransformComponent(Vector2.Zero));
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.2f };
        draw.SetMeshData(new RectangleOutlineMeshGenerator(bounds, thickness: 2f, color: DemoPalette.TextLight));
        boundary.Set(draw);
        boundary.Set<VisibleComponent>();
    }

    /// Returns the player's above-head reaction-mark child (hidden until a `<<react>>` command).
    private Entity CreatePlayer()
    {
        _player = _world.CreateEntity();
        _player.Set(new EntityInfoComponent("Player", "DialogueDemoPlayer"));
        _player.Set(new TransformComponent(PlayerSpawn));
        _player.Set(new PlayerTag());
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.50f };
        draw.SetMeshData(DialogueGlyphs.PlayerShape(PlayerBodyRadius));
        _player.Set(draw);
        _player.Set<VisibleComponent>();

        return CreateReactionChild(_player, new Vector2(0, -2f * PlayerBodyRadius - 20f));
    }

    /// Creates an NPC mesh character at <paramref name="position"/> (feet at the transform) with an
    /// above-head reaction-mark child and an "in range" interact banner. Returns the NPC entity via
    /// <paramref name="npc"/> and (its reaction-mark child, its banner part entities).
    private (Entity mark, Entity[] promptParts) CreateNpc(
        out Entity npc, Vector2 position, IMeshGenerator shape, string infoName,
        float bodyRadius)
    {
        npc = _world.CreateEntity();
        npc.Set(new EntityInfoComponent("NPC", infoName));
        npc.Set(new TransformComponent(position));
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.50f };
        draw.SetMeshData(shape);
        npc.Set(draw);
        npc.Set<VisibleComponent>();

        var mark = CreateReactionChild(npc, new Vector2(0, -2f * bodyRadius - 18f));

        // Lifted a few pixels higher than the old single-line prompt for breathing room.
        var promptParts = CreatePromptBanner(npc, new Vector2(0, -2f * bodyRadius - 44f));

        return (mark, promptParts);
    }

    // The above-head interact banner ("[E] to talk"): a black background rectangle, a keyboard
    // key-cap square (outline + fill) with a white "E", and a white "to talk" label. Several Main-
    // target children of the NPC, all parented and toggled together by NpcInteractionSystem.
    private const float PromptLabelScale = 0.22f;   // smaller than the old 0.26 single-line prompt
    private const float PromptCapPixels = 28f;      // the key-cap square
    private const float PromptCapLabelScale = 0.20f;
    private const float PromptGap = 8f;             // between cap and label
    private const float PromptPadX = 9f;            // black-bg padding
    private const float PromptPadY = 6f;
    // Layer depths above the field/NPC: bg < cap < letter/label (the old prompt sat at ~0.63).
    private const float PromptBgDepth = 0.63f;
    private const float PromptCapDepth = 0.64f;
    private const float PromptTextDepth = 0.65f;

    /// Builds the key-cap "[E] to talk" banner as children of <paramref name="parent"/>, centred
    /// over the head at <paramref name="anchor"/> (the local bottom-centre of the banner). Returns
    /// every part entity so NpcInteractionSystem can show/hide them together. No VisibleComponent
    /// is set here — the banner is hidden until the player is in range.
    private Entity[] CreatePromptBanner(Entity parent, Vector2 anchor)
    {
        const string label = "to talk";
        var capStyle = new KeyCapStyle();  // reuse the sidebar key-cap colours/spacing for consistency

        var labelMeasured = _font.MeasureString(label);
        var labelSize = new Vector2(labelMeasured.Width * PromptLabelScale, labelMeasured.Height * PromptLabelScale);
        var contentWidth = PromptCapPixels + PromptGap + labelSize.X;
        var contentHeight = MathHelper.Max(PromptCapPixels, labelSize.Y);
        var bannerWidth = contentWidth + PromptPadX * 2f;
        var bannerHeight = contentHeight + PromptPadY * 2f;

        // Origin at the banner's top-left, centred horizontally over the head and sitting just above
        // the anchor (its bottom edge at the anchor's Y).
        var topLeft = anchor + new Vector2(-bannerWidth / 2f, -bannerHeight);

        // Black background rectangle behind the whole banner.
        var bg = _world.CreateEntity();
        bg.Set(new EntityInfoComponent("DialogueDemo", "PromptBg"));
        bg.Set(new TransformComponent(topLeft));
        bg.SetParent(parent);
        var bgDraw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = PromptBgDepth };
        bgDraw.SetMeshData(new FilledRectangleMeshGenerator(
            new Rectangle(0, 0, (int)bannerWidth, (int)bannerHeight), Color.Black));
        bg.Set(bgDraw);

        // Key-cap square (outline + fill) — vertically centred in the content row.
        var capOffset = topLeft + new Vector2(PromptPadX, PromptPadY + (contentHeight - PromptCapPixels) / 2f);
        var cap = _world.CreateEntity();
        cap.Set(new EntityInfoComponent("DialogueDemo", "PromptCap"));
        cap.Set(new TransformComponent(capOffset));
        cap.SetParent(parent);
        var capDraw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = PromptCapDepth };
        capDraw.SetMeshData(ShapeBuilder.Panel(
            new Rectangle(0, 0, (int)PromptCapPixels, (int)PromptCapPixels),
            capStyle.FillColor, capStyle.OutlineColor, capStyle.OutlineThickness));
        cap.Set(capDraw);

        // The "E" — white, centred inside the cap.
        var capLetterMeasured = _font.MeasureString("E");
        var capLetterSize = new Vector2(capLetterMeasured.Width * PromptCapLabelScale,
                                        capLetterMeasured.Height * PromptCapLabelScale);
        var capLetterOffset = capOffset + new Vector2(
            (PromptCapPixels - capLetterSize.X) / 2f,
            (PromptCapPixels - capLetterSize.Y) / 2f);
        var letter = _world.CreateEntity();
        letter.Set(new EntityInfoComponent("DialogueDemo", "PromptCapLetter"));
        letter.Set(new TransformComponent(capLetterOffset));
        letter.SetParent(parent);
        letter.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = PromptTextDepth,
            TextContent = "E",
            Font = _font,
            Color = Color.White,
            Scale = PromptCapLabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        letter.Set(new DrawComponent { Type = DrawElementType.Text, Target = RenderTargetID.Main });

        // The "to talk" label — white, to the right of the cap, vertically centred in the row.
        var labelOffset = topLeft + new Vector2(
            PromptPadX + PromptCapPixels + PromptGap,
            PromptPadY + (contentHeight - labelSize.Y) / 2f);
        var labelEntity = _world.CreateEntity();
        labelEntity.Set(new EntityInfoComponent("DialogueDemo", "PromptLabel"));
        labelEntity.Set(new TransformComponent(labelOffset));
        labelEntity.SetParent(parent);
        labelEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = PromptTextDepth,
            TextContent = label,
            Font = _font,
            Color = Color.White,
            Scale = PromptLabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        labelEntity.Set(new DrawComponent { Type = DrawElementType.Text, Target = RenderTargetID.Main });

        // No VisibleComponent yet — NpcInteractionSystem toggles all parts when in range.
        return new[] { bg, cap, letter, labelEntity };
    }

    /// An above-head reaction-mark mesh, child of a character on the Main target. Starts with an
    /// empty mesh and no `VisibleComponent`; `ReactionMarkSystem` fills the glyph and toggles the
    /// tag (Main respects culling/visibility, so the tag controls it here).
    private Entity CreateReactionChild(Entity parent, Vector2 localOffset)
    {
        var mark = _world.CreateEntity();
        mark.Set(new EntityInfoComponent("DialogueDemo", "Reaction"));
        mark.Set(new TransformComponent(localOffset));
        mark.SetParent(parent);
        mark.Set(new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.62f });
        // No VisibleComponent yet — ReactionMarkSystem toggles it on a <<react>> command.
        return mark;
    }

    /// Creates the single in-box emote portrait — a mesh frame (white outline, black fill) with
    /// the speaker's mesh face glyph inside — centred in the dialogue box's left gutter (see
    /// DialogueSystem.PortraitGutterBounds). Both start with empty meshes; DialoguePortraitSystem
    /// fills them on an &lt;&lt;emote&gt;&gt; command and empties them on dialogue end.
    private PortraitSlot CreatePortraitSlot(Rectangle gutter)
    {
        var frameTopLeft = new Vector2(
            gutter.X + (gutter.Width - FrameRenderSize) / 2f,
            gutter.Y + (gutter.Height - FrameRenderSize) / 2f);
        var faceOffset = (FrameRenderSize - FaceRenderSize) / 2f;

        // Frame — a mesh panel on UI, empty until the first <<emote>>; VisibleComponent kept on so
        // MeshPrepSystem keeps its matrix fresh (UI renders regardless of the tag).
        var frame = _world.CreateEntity();
        frame.Set(new EntityInfoComponent("DialogueDemo", "PortraitFrame"));
        frame.Set(new TransformComponent(frameTopLeft));
        frame.Set(new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.UI, LayerDepth = 0.92f });
        frame.Set<VisibleComponent>();

        // Speaker face — a mesh glyph on UI, drawn on top of the frame window.
        var face = _world.CreateEntity();
        face.Set(new EntityInfoComponent("DialogueDemo", "PortraitFace"));
        face.Set(new TransformComponent(frameTopLeft + new Vector2(faceOffset, faceOffset)));
        face.Set(new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.UI, LayerDepth = 0.93f });
        face.Set<VisibleComponent>();

        return new PortraitSlot(frame, face);
    }

    // ─── HUD ────────────────────────────────────────────────────────────────

    private void BuildHud(ContentManager content)
    {
        DemoHeader.Build(
            _world, _viewportManager, _font,
            title: "dialogue",
            descriptionLines: new[]
            {
                "WASD to walk. Press E by the cow (left) or bird (right) to talk.",
                "Up and down arrows choose a reply, E to pick it.",
                "Cow: a box at the bottom. Bird: a balloon over its head.",
            });

        BuildSidebar();
    }

    private void BuildSidebar()
    {
        var capStyle = new KeyCapStyle
        {
            CapPixels = 42,
            CapLabelScale = 0.22f,
        };
        var rowStyle = new KeyRowStyle
        {
            LabelColor = DemoPalette.TextLight,
            HoverColor = DemoPalette.TextHover,
            ActiveColor = DemoPalette.TextSelected,
            LabelScale = 0.18f,
            Gap = 10f,
            BackgroundColor = DemoPalette.DarkBgSecondary,
            HoverBackgroundColor = DemoPalette.DarkBgSecondary,
            ActiveBackgroundColor = DemoPalette.DarkBgSecondary,
            BackgroundPaddingX = 10f,
            BackgroundPaddingY = 6f,
        };

        (Entity Container, Entity Outline, Vector2 Size) Row(string id, string key, string label) =>
            _world.CreateKeyRow(id, key, label, _font, capStyle, rowStyle, layerDepth: 0.96f);

        var talk = Row("hint.talk", "E", "Interact");
        var reset = Row("hint.reset", "R", "reset");

        const float rowGap = 6f;

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopLeft, RenderTargetID.HUD)
            .Direction(LayoutDirection.Vertical)
            .Gap(rowGap)
            .Padding(20 /* top */, 12 /* right */, 12 /* bottom */, 12 /* left */)
            .AlignCross(CrossAxisAlignment.Start)
            .AddSlot(slot => slot.Attach(talk.Container).MeasureWith(_ => talk.Size))
            .AddSlot(slot => slot.Attach(reset.Container).MeasureWith(_ => reset.Size))
            .Build();
    }

    // ─── messages / bridges ───────────────────────────────────────────────────

    private void OnButtonClicked(in DemoButtonClicked msg)
    {
        switch (msg.Id)
        {
            case DemoHeader.BackId: _screenController?.LoadScreen(DemoScreens.Launcher); break;
            case DemoHeader.ExitId: _screenController?.Game.Exit(); break;
        }
    }

    private void OnDialogueActive(in DialogueActiveMessage msg) => _dialogueActive = msg.IsActive;

    public bool DialogueActive => _dialogueActive;

    public void GoBackToLauncher() => _screenController?.LoadScreen(DemoScreens.Launcher);

    /// Returns the player to the spawn point (e.g. after wandering off). Harmless during a
    /// conversation since movement is frozen then.
    public void ResetPlayer()
    {
        if (_player.IsAlive) _player.Get<TransformComponent>().Position = PlayerSpawn;
    }

    /// The WORLD-space safe rectangle the bird's anchored balloon must stay inside: the camera's
    /// current view, with the TOP inset past the HUD header band and small side/bottom margins so
    /// the balloon clears the centered top header (a HUD overlay outside camera bounds). At zoom 1
    /// one world unit is one virtual pixel, so the header band height applies directly.
    private Rectangle BirdBalloonSafeArea()
    {
        const int sideMargin = 16;
        const int bottomMargin = 16;
        var view = _camera.VirtualScreenBounds;
        var topInset = (int)(HeaderBandHeight / _camera.Zoom);
        return new Rectangle(
            view.Left + sideMargin,
            view.Top + topInset,
            view.Width - 2 * sideMargin,
            view.Height - topInset - bottomMargin);
    }

    // ─── pipeline ────────────────────────────────────────────────────────────

    private SequentialSystem<GameState> CreateUpdateSystem(
        DialogueSystem cowDialogue, DialogueSystem birdDialogue, NpcInteractionTarget[] npcTargets,
        ReactionMarkSystem reactionSystem, DialoguePortraitSystem portraitSystem)
    {
        var cursorInputSystem = new CursorInputSystem(_world, _viewportManager);

        // The editor overlay (see DemoEditor): built over THIS screen's world/camera/layers.
        _editor = DemoEditor.TryCreate(_editorEnabled, _world, _camera, _layers, _content,
            _graphicsDevice, _spriteBatch, _viewportManager, () => _screenController?.Game);
        // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
        if (_editor?.Overlay.HasEditorOpPlan == true) cursorInputSystem.SkipHardwareRead = true;

        // ---- Weave the update pipeline through the registrar. With the editor off every gate
        // is a pass-through in Play and the order matches the pre-editor screen exactly. ----
        var p = _updatePipeline;
        // Feeds interact/up/down each frame — Play-only input, frozen with the simulation.
        p.Add("demoInput", new DialogueDemoInputSystem(_interact, _up, _down), EditTimeBehavior.Freeze);
        p.Add("input", cursorInputSystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.keys", _editor.Keys, EditTimeBehavior.RunNormally);
            p.Add("editor.sceneReader", _editor.Overlay.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Overlay.Dialog, EditTimeBehavior.RunNormally);
            p.Add("editor.contextMenu", _editor.Overlay.Menu, EditTimeBehavior.RunNormally);
        }
        p.AddGroup("layout", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("intrinsicSizing", new IntrinsicSizingSystem(_world));
            g.Add("autoLayout", new AutoLayoutSystem(_world, _viewportManager));
        });
        // Demo UI interaction FREEZES in Edit: a click belongs to the editor, never to
        // back / exit (which would tear the screen down mid-editing).
        p.Add("ui.interaction", new DemoButtonInteractionSystem(_world), EditTimeBehavior.Freeze);
        // The demo simulation freezes in Edit: movement, NPC triggers, both dialogue engines,
        // reactions/portraits, the text reveal, and the demo shortcuts (Escape = back) all
        // mutate the scene (or tear the screen down) per frame. One Freeze gate on the group.
        p.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("movement", new PlayerMovementSystem(_world, BoundaryHalfWidth, BoundaryHalfHeight,
                PlayerSpeed, PlayerBodyRadius, 2f * PlayerBodyRadius, () => _dialogueActive));
            g.Add("npcInteraction", new NpcInteractionSystem(
                _world, _player, npcTargets, _interact, InteractRange, () => _dialogueActive));
            g.Add("dialogueCow", cowDialogue);   // node "Start" → fixed bottom box; routes by node ownership
            g.Add("dialogueBird", birdDialogue); // node "Bird"  → over-head anchored balloon
            g.Add("reactions", reactionSystem);
            g.Add("portraits", portraitSystem);
            g.Add("textUpdate", new TextUpdateSystem(_world)); // advance the reveal animation
            g.Add("shortcuts", new DialogueDemoShortcutSystem(this));
        });
        if (_editor != null)
        {
            p.Add("editor.commands", _editor.Overlay.EditorCommands, EditTimeBehavior.RunNormally);
            p.Add("editor.gizmo", _editor.Overlay.Gizmo, EditTimeBehavior.RunNormally);
            p.Add("editor.proxySync", _editor.Overlay.ProxySync, EditTimeBehavior.RunNormally);
        }
        p.Add("hierarchy", new HierarchySystem(_world), EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.AddGroup("editor.toolbar", EditTimeBehavior.RunNormally, g =>
            {
                g.Add("meshPrep", _editor.Overlay.ToolbarMeshPrep);
                g.Add("clicks", _editor.Overlay.ToolbarClicks);
            });
            p.Add("editor.systemsPanel", _editor.Overlay.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.Overlay.CameraNav, EditTimeBehavior.RunNormally);
        }
        p.Add("cursorPosition", new CursorPositionSystem(_world, _camera, _viewportManager),
            EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.shell", _editor.Overlay.Shell, EditTimeBehavior.RunNormally);
            if (_editor.Overlay.EditorOpDriver != null)
                p.Add("editor.opDriver", _editor.Overlay.EditorOpDriver, EditTimeBehavior.RunNormally);
        }

        return p.Build();
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var renderLayers = new List<RenderLayer>
        {
            RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
            RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
            RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
        };
        if (_editor != null)
            renderLayers.Add(_editor.Overlay.ChromeLayer);

        // ---- Weave the draw pipeline through the registrar (retained for the systems panel). ----
        var p = _drawPipeline;
        // With the editor composed, the sprite prep chain (cull → sprite prep → Y-sort) is added
        // so a native scene loaded while editing actually previews; the demo DrawLayerMap has no
        // Y-sorted layer, so YSortSystem passes depths through — documented graceful degradation.
        // (No demo entity carries SpriteInfoComponent, so CullingSystem never touches the
        // manually-toggled prompt/mark meshes.)
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            if (_editorEnabled) g.Add("culling", new CullingSystem(_world, _camera));
            g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false));
            if (_editorEnabled) g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering: false));
            g.Add("meshPrep", new MeshPrepSystem(_world));
            g.Add("buttonMeshPrep", new ButtonMeshPrepSystem(_world));
        });
        if (_editor != null)
        {
            p.Add("editor.selection", _editor.Overlay.Selection, EditTimeBehavior.RunNormally);
            p.Add("editor.overlayPrep", _editor.Overlay.OverlayPrep, EditTimeBehavior.RunNormally);
        }
        p.Add("renderMain", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera), EditTimeBehavior.RunNormally);
        p.Add("renderUI", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI]), EditTimeBehavior.RunNormally);
        p.Add("renderHUD", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]), EditTimeBehavior.RunNormally);
        if (_editor != null)
            p.Add("editor.renderChrome", _editor.Overlay.ChromeRender, EditTimeBehavior.RunNormally);
        p.Add("finalDraw", new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, renderLayers),
            EditTimeBehavior.RunNormally);

        return p.Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }


    // ─── in-memory Yarn ──────────────────────────────────────────────────────

    /// The conversation. Lines use Yarn's "Speaker: text" form (DialogueSystem splits the
    /// speaker off). `<<emote who kind>>` drives the in-box portrait (left = NPC, right =
    /// player); `<<react who mark>>` pops an above-head reaction mark. Both arrive as a
    /// DialogueCommandMessage. The start node must be named "Start" (see NpcInteractionSystem).
    private const string YarnSource = @"title: Start
---
<<emote npc happy>>
Cow: Moo! A visitor! We don't get many folks wandering out to my little meadow.
Cow: What brings you all the way out here, friend?
<<emote player question>>
-> I'm just exploring the area.
    <<emote npc happy>>
    Cow: Exploring! Wonderful. There's no finer patch of grass this side of the river.
    <<emote player happy>>
    <<react player star>>
    Player: It really is peaceful out here.
-> Do you ever get lonely?
    <<emote npc sad>>
    Cow: ...Sometimes. The days are long and the field is wide.
    <<emote npc happy>>
    <<react npc star>>
    Cow: But a good chat like this one? That keeps me going.
-> Wait, can cows really talk?
    <<emote player surprised>>
    <<react player question>>
    Player: Wait... can cows actually talk?
    <<emote npc surprised>>
    <<react npc exclaim>>
    Cow: Can't yours?
    <<emote player surprised>>
    Player: ...Fair enough.
<<emote npc happy>>
Cow: Do come visit again. The meadow's always right here.
<<react npc star>>
===
";

    /// The bird conversation, played by the anchored (over-head balloon) DialogueSystem. Kept
    /// short — lines and options fit the compact bubble. Only `<<react>>` commands are used (the
    /// balloon has no portrait gutter); the start node must be named "Bird" (see the npc targets).
    private const string BirdYarnSource = @"title: Bird
---
<<react bird exclaim>>
Robin: Tweet! Up here! Mind your step down there, friend.
Robin: Not many two-leggers stop to say hello. What's on your mind?
-> Nice view from up here?
    <<react bird star>>
    Robin: The best! The whole meadow, and the cow's silly face.
-> Seen anything interesting?
    <<react player question>>
    Robin: A shiny pebble by the fence. I may have borrowed it.
    <<react bird star>>
-> Just saying hi.
    <<react player exclaim>>
    Robin: Ha! A bird does love good manners. Tweet!
Robin: Off I go. Mind the puddles!
<<react bird star>>
===
";

    /// Compiles a Yarn source string into a runtime <see cref="YarnProgram"/>, mirroring
    /// what <c>YarnSpinnerProcessor</c> does at content-build time — so the demo ships no
    /// .yarn asset. The required deps (Yarn.Compiler, Google.Protobuf, CsvHelper) flow in
    /// transitively from MonoDreams.
    private static YarnProgram CompileYarn(string source)
    {
        var job = CompilationJob.CreateFromString("DemoDialogue", source);
        var result = Compiler.Compile(job);
        if (result.Program == null)
            throw new InvalidOperationException(
                "Demo Yarn script failed to compile: " + string.Join("; ", result.Diagnostics));

        var program = new YarnProgram();

        using (var memoryStream = new MemoryStream())
        using (var outputStream = new CodedOutputStream(memoryStream))
        {
            result.Program.WriteTo(outputStream);
            outputStream.Flush();
            program.CompiledProgram = memoryStream.ToArray();
        }

        using (var memoryStream = new MemoryStream())
        {
            using var textWriter = new StreamWriter(memoryStream);
            var csv = new CsvWriter(textWriter, new CsvConfiguration(CultureInfo.InvariantCulture));
            var lines = result.StringTable!.Select(x => new
            {
                id = x.Key,
                text = x.Value.text,
                file = x.Value.fileName,
                node = x.Value.nodeName,
                lineNumber = x.Value.lineNumber,
            });
            csv.WriteRecords(lines);
            textWriter.Flush();
            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
            program.BaseLocalizationId = CultureInfo.CurrentCulture.Name;
            program.BaseLocalisationStringTable = reader.ReadToEnd();
            program.Localizations = new YarnTranslation[0];
        }

        return program;
    }
}

/// Tag for the player-controlled character.
public struct PlayerTag { }

/// Per-emote-bubble lifetime. <see cref="HideAt"/> is the GameState.TotalTime at which the
/// bubble auto-hides; float.NaN means "just shown — initialise on the next Update".
public struct EmoteBubbleComponent { public float HideAt; }

/// WASD/arrow movement for the tagged player (a mesh shape, feet at the transform), clamped so
/// the body stays inside the boundary. Frozen while a dialogue is active.
[With(typeof(PlayerTag), typeof(TransformComponent))]
public sealed class PlayerMovementSystem : AEntitySetSystem<GameState>
{
    private readonly float _halfWidth;
    private readonly float _halfHeight;
    private readonly float _speed;
    private readonly float _clampHalfWidth;
    private readonly float _clampHeight;
    private readonly Func<bool> _isFrozen;

    public PlayerMovementSystem(World world, float halfWidth, float halfHeight, float speed,
        float clampHalfWidth, float clampHeight, Func<bool> isFrozen)
        : base(world)
    {
        _halfWidth = halfWidth;
        _halfHeight = halfHeight;
        _speed = speed;
        _clampHalfWidth = clampHalfWidth;
        _clampHeight = clampHeight;
        _isFrozen = isFrozen;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        if (_isFrozen()) return;

        var keyboard = Keyboard.GetState();
        var dir = Vector2.Zero;
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left)) dir.X -= 1f;
        if (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right)) dir.X += 1f;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up)) dir.Y -= 1f;
        if (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)) dir.Y += 1f;

        if (dir == Vector2.Zero) return;

        dir.Normalize();
        var transform = entity.Get<TransformComponent>();
        var next = transform.Position + dir * _speed * state.Time;
        // Feet anchored at the position; keep the whole body (which rises _clampHeight above the
        // feet) inside the boundary.
        next.X = MathHelper.Clamp(next.X, -_halfWidth + _clampHalfWidth, _halfWidth - _clampHalfWidth);
        next.Y = MathHelper.Clamp(next.Y, -_halfHeight + _clampHeight, _halfHeight);
        transform.Position = next;
    }
}

/// One interactable NPC for <see cref="NpcInteractionSystem"/>: the character entity, the entities
/// making up its above-head key-cap banner (all toggled together), and the Yarn node its
/// <see cref="DialogueStartMessage"/> kicks off.
public readonly record struct NpcInteractionTarget(Entity Npc, Entity[] PromptParts, string StartNode);

/// Shows a "press E" prompt when the player is near any NPC and, on the interact edge, publishes a
/// <see cref="DialogueStartMessage"/> for the nearest in-range one (node-ownership routing then
/// delivers it to the right DialogueSystem). Suppressed while any dialogue is active.
public sealed class NpcInteractionSystem : ISystem<GameState>
{
    private readonly Entity _player;
    private readonly NpcInteractionTarget[] _targets;
    private readonly AInputState _interact;
    private readonly float _rangeSq;
    private readonly Func<bool> _dialogueActive;
    private readonly World _world;

    public bool IsEnabled { get; set; } = true;

    public NpcInteractionSystem(World world, Entity player, NpcInteractionTarget[] targets,
        AInputState interact, float range, Func<bool> dialogueActive)
    {
        _world = world;
        _player = player;
        _targets = targets;
        _interact = interact;
        _rangeSq = range * range;
        _dialogueActive = dialogueActive;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled || !_player.IsAlive) return;

        if (_dialogueActive())
        {
            foreach (var t in _targets) SetPromptVisible(t.PromptParts, false);
            return;
        }

        var playerPos = _player.Get<TransformComponent>().Position;
        var bestDistSq = _rangeSq;
        NpcInteractionTarget? nearest = null;

        foreach (var t in _targets)
        {
            if (!t.Npc.IsAlive) { SetPromptVisible(t.PromptParts, false); continue; }
            var distSq = Vector2.DistanceSquared(playerPos, t.Npc.Get<TransformComponent>().Position);
            var inRange = distSq <= _rangeSq;
            SetPromptVisible(t.PromptParts, inRange);
            if (inRange && distSq <= bestDistSq) { bestDistSq = distSq; nearest = t; }
        }

        if (nearest is { } target && _interact.JustPressed())
            _world.Publish(new DialogueStartMessage(target.Npc, target.StartNode));
    }

    // The banner is several Main-target child entities (bg, cap, letter, label); Main gates each on
    // its own VisibleComponent, so all parts toggle together.
    private static void SetPromptVisible(Entity[] promptParts, bool visible)
    {
        foreach (var part in promptParts)
        {
            if (!part.IsAlive) continue;
            var has = part.Has<VisibleComponent>();
            if (visible && !has) part.Set<VisibleComponent>();
            else if (!visible && has) part.Remove<VisibleComponent>();
        }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// A pair of entities making up one in-box portrait: a wood-brown frame (mesh) and the emote
/// face (sprite) drawn on top of it.
public readonly record struct PortraitSlot(Entity Frame, Entity Face);

/// Above-head reaction marks. Subscribes to <see cref="DialogueCommandMessage"/>, reacts to
/// `react who mark` by showing a generated mesh glyph above the keyed character (`player`, `npc`,
/// `bird`, …) on the Main target, and auto-hides it after a delay. Clears on dialogue end.
public sealed class ReactionMarkSystem : ISystem<GameState>
{
    private readonly Dictionary<string, Entity> _marks;
    private readonly float _duration;
    private readonly float _markSize;

    public bool IsEnabled { get; set; } = true;

    public ReactionMarkSystem(World world, Dictionary<string, Entity> marks,
        float duration, float markSize)
    {
        _marks = marks;
        _duration = duration;
        _markSize = markSize;
        world.Subscribe(this);
    }

    [Subscribe]
    private void OnCommand(in DialogueCommandMessage msg)
    {
        var parts = msg.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "react") return;

        if (!_marks.TryGetValue(parts[1], out var target) || !target.IsAlive) return;

        target.Set(new EmoteBubbleComponent { HideAt = float.NaN });
        if (!target.Has<VisibleComponent>()) target.Set<VisibleComponent>();
        target.Get<DrawComponent>().SetMeshData(DialogueGlyphs.ReactionMark(_markSize, parts[2]));
    }

    [Subscribe]
    private void OnDialogueActive(in DialogueActiveMessage msg)
    {
        if (!msg.IsActive)
            foreach (var mark in _marks.Values) Hide(mark);
    }

    public void Update(GameState state)
    {
        foreach (var mark in _marks.Values) Expire(mark, state.TotalTime);
    }

    private void Expire(Entity mark, float now)
    {
        if (!mark.IsAlive || !mark.Has<EmoteBubbleComponent>()) return;
        ref var bubble = ref mark.Get<EmoteBubbleComponent>();
        if (float.IsNaN(bubble.HideAt)) { bubble.HideAt = now + _duration; return; }
        if (now >= bubble.HideAt) Hide(mark);
    }

    // Main-target child — removing VisibleComponent is enough to hide it.
    private static void Hide(Entity mark)
    {
        if (!mark.IsAlive) return;
        if (mark.Has<VisibleComponent>()) mark.Remove<VisibleComponent>();
        if (mark.Has<EmoteBubbleComponent>()) mark.Remove<EmoteBubbleComponent>();
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// In-box speaker portrait. Subscribes to <see cref="DialogueCommandMessage"/>; on
/// `emote who kind` it shows the framed mesh portrait of whoever is speaking in the single emote
/// frame (the box's left gutter). The NPC uses a cow-face glyph (`kind` ignored); the player
/// picks an emote-face glyph by `kind`. The portrait persists until the next `emote` or the
/// dialogue ends — it does not auto-expire (unlike reaction marks). Frame + face live on the UI
/// target, which always renders, so hiding empties the meshes rather than toggling VisibleComponent.
public sealed class DialoguePortraitSystem : ISystem<GameState>
{
    private readonly PortraitSlot _slot;
    private readonly MeshData _frameMesh;   // the static frame panel (shown on any emote)
    private readonly float _faceSize;

    public bool IsEnabled { get; set; } = true;

    public DialoguePortraitSystem(World world, PortraitSlot slot, float frameSize, float faceSize)
    {
        _slot = slot;
        _faceSize = faceSize;
        _frameMesh = ShapeBuilder.Panel(
            new Rectangle(0, 0, (int)frameSize, (int)frameSize), Color.Black, Color.White, 2f).Generate();
        world.Subscribe(this);
    }

    [Subscribe]
    private void OnCommand(in DialogueCommandMessage msg)
    {
        var parts = msg.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "emote") return;

        switch (parts[1])
        {
            case "npc":
                ShowFace(DialogueGlyphs.CowFace(_faceSize).Generate()); // cow face, regardless of kind
                break;
            case "player":
                ShowFace(DialogueGlyphs.EmoteFace(_faceSize, parts[2]).Generate());
                break;
        }
    }

    [Subscribe]
    private void OnDialogueActive(in DialogueActiveMessage msg)
    {
        if (!msg.IsActive)
        {
            ClearMesh(_slot.Frame);
            ClearMesh(_slot.Face);
        }
    }

    public void Update(GameState state) { }

    private void ShowFace(MeshData faceMesh)
    {
        // Show the (static) frame on the first emote, then swap in the speaker's face glyph.
        if (_slot.Frame.IsAlive) _slot.Frame.Get<DrawComponent>().SetMeshData(_frameMesh);
        if (_slot.Face.IsAlive) _slot.Face.Get<DrawComponent>().SetMeshData(faceMesh);
    }

    private static void ClearMesh(Entity e)
    {
        if (!e.IsAlive || !e.Has<DrawComponent>()) return;
        ref var draw = ref e.Get<DrawComponent>();
        draw.Type = DrawElementType.Mesh;
        draw.Vertices = [];
        draw.Indices = [];
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// Maps keyboard keys to the dialogue's interact/up/down <see cref="AInputState"/>s, updating
/// them once per frame so DialogueSystem and NpcInteractionSystem see fresh edges.
public sealed class DialogueDemoInputSystem : AKeyboardInputHandlingSystem
{
    public override List<(AInputState inputState, Keys)> InputMapping { get; }

    public DialogueDemoInputSystem(AInputState interact, AInputState up, AInputState down)
    {
        InputMapping = new List<(AInputState inputState, Keys)>
        {
            (interact, Keys.E),
            (interact, Keys.Enter),
            (interact, Keys.Space),
            (up, Keys.Up),
            (up, Keys.W),
            (down, Keys.Down),
            (down, Keys.S),
        };
    }
}

/// Edge-triggered screen shortcuts: R resets the player, Escape returns to the launcher.
public sealed class DialogueDemoShortcutSystem : ISystem<GameState>
{
    private readonly DialogueDemoScreen _screen;
    private KeyboardState _previous;
    public bool IsEnabled { get; set; } = true;

    public DialogueDemoShortcutSystem(DialogueDemoScreen screen)
    {
        _screen = screen;
        _previous = Keyboard.GetState();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        var current = Keyboard.GetState();
        bool Pressed(Keys k) => current.IsKeyDown(k) && !_previous.IsKeyDown(k);

        if (Pressed(Keys.R)) _screen.ResetPlayer();
        if (Pressed(Keys.Escape)) _screen.GoBackToLauncher();

        _previous = current;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// Concrete <see cref="AInputState"/> for the dialogue demo's per-action input.
public sealed class DemoInputState : AInputState { }

/// Generated mesh glyphs for the dialogue scene — replaces the Sprout Lands character /
/// emote / icon sprites. World shapes are authored with the character's feet at the local
/// origin (body rising toward -Y); portrait faces fill a (0..size) box; reaction marks are
/// centred on the local origin.
internal static class DialogueGlyphs
{
    private static readonly Color Ink    = new(28, 30, 38);     // outlines, eyes, details
    private static readonly Color Player = new(86, 160, 196);   // player body
    private static readonly Color Cow    = new(224, 226, 230);  // cow body
    private static readonly Color Snout  = new(232, 160, 176);  // cow snout (pink)
    private static readonly Color Skin   = new(250, 224, 196);  // emote face
    private static readonly Color Bird   = new(240, 196, 96);   // bird body (warm yellow)
    private static readonly Color Beak   = new(232, 140, 60);   // bird beak (orange)

    /// World player: a round body with two eyes, feet at the origin.
    public static IMeshGenerator PlayerShape(float r) =>
        new CompositeMeshGenerator()
            .Add(new CircleMeshGenerator(new Vector2(0, -r), r, Player, 28))
            .Add(new CircleOutlineMeshGenerator(new Vector2(0, -r), r, 2.5f, Ink, 28))
            .Add(new CircleMeshGenerator(new Vector2(-0.30f * r, -1.4f * r), 0.12f * r, Ink, 10))
            .Add(new CircleMeshGenerator(new Vector2(0.30f * r, -1.4f * r), 0.12f * r, Ink, 10));

    /// World cow: a larger pale body with horns, a pink snout and eyes, feet at the origin.
    public static IMeshGenerator CowShape(float r) =>
        new CompositeMeshGenerator()
            .Add(new CircleMeshGenerator(new Vector2(0, -r), r, Cow, 28))
            .Add(new CircleOutlineMeshGenerator(new Vector2(0, -r), r, 2.5f, Ink, 28))
            .Add(new FilledTriangleMeshGenerator(
                new Vector2(-0.75f * r, -1.6f * r), new Vector2(-0.40f * r, -1.7f * r), new Vector2(-0.58f * r, -2.05f * r), Ink))
            .Add(new FilledTriangleMeshGenerator(
                new Vector2(0.75f * r, -1.6f * r), new Vector2(0.40f * r, -1.7f * r), new Vector2(0.58f * r, -2.05f * r), Ink))
            .Add(new CircleMeshGenerator(new Vector2(0, -0.55f * r), 0.40f * r, Snout, 18))
            .Add(new CircleOutlineMeshGenerator(new Vector2(0, -0.55f * r), 0.40f * r, 2f, Ink, 18))
            .Add(new CircleMeshGenerator(new Vector2(-0.35f * r, -1.35f * r), 0.11f * r, Ink, 10))
            .Add(new CircleMeshGenerator(new Vector2(0.35f * r, -1.35f * r), 0.11f * r, Ink, 10));

    /// World bird: a small round body with a head, an orange beak (facing right) and an eye,
    /// feet at the origin. The upper-right NPC whose dialogue floats above its head.
    public static IMeshGenerator BirdShape(float r) =>
        new CompositeMeshGenerator()
            .Add(new CircleMeshGenerator(new Vector2(0, -r), r, Bird, 26))                          // body
            .Add(new CircleOutlineMeshGenerator(new Vector2(0, -r), r, 2.5f, Ink, 26))
            .Add(new CircleMeshGenerator(new Vector2(0, -1.85f * r), 0.62f * r, Bird, 24))          // head
            .Add(new CircleOutlineMeshGenerator(new Vector2(0, -1.85f * r), 0.62f * r, 2.5f, Ink, 24))
            .Add(new FilledTriangleMeshGenerator(                                                   // beak (points right)
                new Vector2(0.55f * r, -1.92f * r), new Vector2(1.02f * r, -1.78f * r), new Vector2(0.55f * r, -1.64f * r), Beak))
            .Add(new CircleMeshGenerator(new Vector2(0.20f * r, -2.02f * r), 0.10f * r, Ink, 10))   // eye
            .Add(new FilledTriangleMeshGenerator(                                                   // little wing
                new Vector2(-0.20f * r, -0.95f * r), new Vector2(-0.95f * r, -1.15f * r), new Vector2(-0.30f * r, -1.55f * r), new Color(226, 176, 78)));

    /// Player emote face glyph: a head with eyes and a mouth keyed by <paramref name="kind"/>.
    public static IMeshGenerator EmoteFace(float s, string kind)
    {
        var c = new Vector2(s / 2f, s / 2f);
        var face = new CompositeMeshGenerator()
            .Add(new CircleMeshGenerator(c, 0.40f * s, Skin, 24))
            .Add(new CircleOutlineMeshGenerator(c, 0.40f * s, 2f, Ink, 24))
            .Add(new CircleMeshGenerator(c + new Vector2(-0.16f * s, -0.05f * s), 0.045f * s, Ink, 10))
            .Add(new CircleMeshGenerator(c + new Vector2(0.16f * s, -0.05f * s), 0.045f * s, Ink, 10));

        switch (kind)
        {
            case "sad":
                face.Add(new PolylineMeshGenerator(new[]
                {
                    c + new Vector2(-0.16f * s, 0.18f * s), c + new Vector2(0, 0.10f * s), c + new Vector2(0.16f * s, 0.18f * s),
                }, 2.5f, Ink));
                break;
            case "surprised":
                face.Add(new CircleOutlineMeshGenerator(c + new Vector2(0, 0.13f * s), 0.06f * s, 2f, Ink, 12));
                break;
            case "question":
                face.Add(new PolylineMeshGenerator(new[]
                {
                    c + new Vector2(-0.13f * s, 0.14f * s), c + new Vector2(0.13f * s, 0.14f * s),
                }, 2.5f, Ink));
                break;
            default: // happy / heart
                face.Add(new PolylineMeshGenerator(new[]
                {
                    c + new Vector2(-0.16f * s, 0.10f * s), c + new Vector2(0, 0.18f * s), c + new Vector2(0.16f * s, 0.10f * s),
                }, 2.5f, Ink));
                break;
        }
        return face;
    }

    /// Cow portrait face glyph (centred in a 0..size box).
    public static IMeshGenerator CowFace(float s)
    {
        var c = new Vector2(s / 2f, s / 2f);
        return new CompositeMeshGenerator()
            .Add(new CircleMeshGenerator(c, 0.40f * s, Cow, 24))
            .Add(new CircleOutlineMeshGenerator(c, 0.40f * s, 2f, Ink, 24))
            .Add(new FilledTriangleMeshGenerator(
                c + new Vector2(-0.34f * s, -0.20f * s), c + new Vector2(-0.16f * s, -0.26f * s), c + new Vector2(-0.26f * s, -0.42f * s), Ink))
            .Add(new FilledTriangleMeshGenerator(
                c + new Vector2(0.34f * s, -0.20f * s), c + new Vector2(0.16f * s, -0.26f * s), c + new Vector2(0.26f * s, -0.42f * s), Ink))
            .Add(new CircleMeshGenerator(c + new Vector2(0, 0.16f * s), 0.16f * s, Snout, 16))
            .Add(new CircleOutlineMeshGenerator(c + new Vector2(0, 0.16f * s), 0.16f * s, 1.5f, Ink, 16))
            .Add(new CircleMeshGenerator(c + new Vector2(-0.16f * s, -0.05f * s), 0.045f * s, Ink, 10))
            .Add(new CircleMeshGenerator(c + new Vector2(0.16f * s, -0.05f * s), 0.045f * s, Ink, 10));
    }

    /// An above-head reaction mark centred on the local origin: exclaim "!", star, or "?".
    public static IMeshGenerator ReactionMark(float s, string mark)
    {
        switch (mark)
        {
            case "star":
                return ShapeBuilder.Star(Vector2.Zero, 0.5f * s, 0.22f * s, 5, new Color(250, 205, 110));
            case "question":
                var cyan = new Color(120, 200, 235);
                return new CompositeMeshGenerator()
                    .Add(new PolylineMeshGenerator(new[]
                    {
                        new Vector2(-0.22f * s, -0.26f * s), new Vector2(-0.04f * s, -0.42f * s),
                        new Vector2(0.18f * s, -0.30f * s), new Vector2(0.16f * s, -0.08f * s),
                        new Vector2(0f, 0.02f * s), new Vector2(0f, 0.16f * s),
                    }, 4f, cyan))
                    .Add(new CircleMeshGenerator(new Vector2(0, 0.34f * s), 0.08f * s, cyan, 10));
            default: // exclaim
                var yellow = new Color(250, 210, 90);
                return new CompositeMeshGenerator()
                    .Add(new FilledRectangleMeshGenerator(
                        new Rectangle((int)(-0.09f * s), (int)(-0.5f * s), (int)(0.18f * s), (int)(0.6f * s)), yellow))
                    .Add(new CircleMeshGenerator(new Vector2(0, 0.4f * s), 0.10f * s, yellow, 10));
        }
    }
}
