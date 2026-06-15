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
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Dialogue;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.Input;
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

/// Dialogue block demo. A very basic top-down Sprout Lands scene: walk the player around
/// a grass field with WASD, approach the cow NPC at the top-left, and press E to start a
/// Yarn conversation. The line text wraps and reveals character-by-character; pick a reply
/// with the up/down arrows and E. Yarn <c>&lt;&lt;emote who kind&gt;&gt;</c> commands show
/// the speaker's framed portrait inside the box (left = NPC, right = player) and
/// <c>&lt;&lt;react who mark&gt;&gt;</c> pops a reaction mark above a character's head.
///
/// Showcases the dialogue block (<see cref="DialogueSystem"/> + Yarn runtime, fed an
/// in-memory-compiled program so the demo ships no .yarn asset), the rendering-text reveal,
/// and sprite rendering on the Main target.
public class DialogueDemoScreen : IGameScreen
{
    private const float BoundaryHalfWidth = 380f;
    private const float BoundaryHalfHeight = 220f;
    private const float TileSize = 40f;          // 19×11 tiles fill the 760×440 boundary exactly
    private const float PlayerSpeed = 135f;       // 0.75× the original
    private const int PlayerFrame = 48;           // Basic Charakter sheet: 4×4 grid of 48×48 (source)
    private const int CowFrame = 32;              // Free Cow sheet: 3×2 grid of 32×32 (source)
    private const float PlayerRenderSize = 96f;   // exact 2× of the 48px source — crisp under PointClamp
    private const float CowRenderSize = 96f;      // exact 3× of the 32px source — crisp under PointClamp
    private const float InteractRange = 170f;
    private const float ReactionDuration = 1.6f;  // above-head reaction marks auto-hide

    // Dialogue-box tuning passed into DialogueSystem (two-layer box: a beige background panel +
    // a lighter inner cream talk balloon, with a left gutter for the framed emote — all sliced
    // from the Sprout Lands UI pack sheet, SproutLands/UI/basic_pack).
    private const float DialogueTextScale = 0.27f;
    private const float DialogueIndicatorSize = 48f;
    private const float DialogueBoxHeight = 150f;
    private const float DialoguePortraitGutter = 140f; // left reserve inside the box for the emote frame
    private const float DialogueBalloonPadding = 14f;

    // The emote portrait: a dark-wood ornate frame (basic_pack) with the speaker's face inside it.
    private static readonly Rectangle FrameSource = new(153, 105, 30, 30); // ornate wood frame cell
    private const float FrameRenderSize = 120f;   // 4× the 30px frame source
    private const float FaceRenderSize = 56f;     // speaker face, centred in the frame window
    // Nine-patch source panels on basic_pack (corner = 8px) for the two box layers:
    private static readonly Rectangle BoxPanelSource = new(259, 180, 90, 25);     // darker tan (background)
    private static readonly Rectangle BalloonPanelSource = new(163, 178, 90, 27); // lighter cream (balloon)

    // The NPC portrait is the cow itself, cropped to its head (it faces right — horns, eyes, pink
    // snout): a lower, wider crop than before so the whole face is framed instead of cut. Tunable.
    private static readonly Rectangle CowHeadSource = new(14, 12, 16, 16);

    private static readonly Vector2 PlayerSpawn = new(40f, 90f);
    // Upper-left, but low enough that the above-head "E to talk" prompt clears the header banner.
    private static readonly Vector2 NpcPosition = new(-300f, -60f);

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
    private bool _dialogueActive;

    public ISystem<GameState> UpdateSystem { get; private set; } = null!;
    public ISystem<GameState> DrawSystem { get; private set; } = null!;
    public World World => _world;

    public DialogueDemoScreen(GraphicsDevice graphicsDevice, ContentManager content,
        MonoDreams.Component.Camera camera, ViewportManager viewportManager, SpriteBatch spriteBatch)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
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

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Cursor/default"),
            [CursorType.Pointer] = content.Load<Texture2D>("Cursor/pointer"),
            [CursorType.Hand] = content.Load<Texture2D>("Cursor/hand"),
        };
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        var emoteSheet = content.Load<Texture2D>("SproutLands/Emotes/emotes");
        var iconSheet = content.Load<Texture2D>("SproutLands/Icons/all_icons");
        var cowSheet = content.Load<Texture2D>("SproutLands/Characters/Free Cow Sprites");
        var uiSheet = content.Load<Texture2D>("SproutLands/UI/basic_pack");

        CreateGround(content);
        CreateBoundary();
        var playerMark = CreatePlayer(content, iconSheet);
        var (npcMark, prompt) = CreateNpc(cowSheet, iconSheet);

        var dialogueSystem = new DialogueSystem(
            _world,
            uiSheet,                                  // box background: a basic_pack panel (see boxNinePatch)
            _font,
            content.Load<Texture2D>("SproutLands/Dialogue/indicator").Crop(new Rectangle(96, 0, 16, 16), _graphicsDevice),
            _viewportManager.VirtualWidth, _viewportManager.VirtualHeight,
            layerDepth: 0.9f,
            _interact, _up, _down,
            new[] { CompileYarn(YarnSource) },
            textScale: DialogueTextScale,
            indicatorSize: DialogueIndicatorSize,
            talkBalloonTexture: uiSheet,                       // inner cream balloon
            talkBalloonNinePatch: Panel9(BalloonPanelSource, 8),
            portraitGutter: DialoguePortraitGutter,
            balloonPadding: DialogueBalloonPadding,
            boxHeight: DialogueBoxHeight,
            boxNinePatch: Panel9(BoxPanelSource, 8));          // darker tan background

        // A single emote frame in the box's left gutter shows whoever is speaking.
        var portrait = CreatePortraitSlot(dialogueSystem.PortraitGutterBounds, uiSheet);

        BuildHud(content);

        // <<react who mark>> → above-head icon mark; <<emote who kind>> → the in-box portrait
        // (cow head for the NPC, emote face for the player). Both arrive as DialogueCommandMessage.
        var reactionSystem = new ReactionMarkSystem(_world, playerMark, npcMark, ReactionIcons, ReactionDuration);
        var portraitSystem = new DialoguePortraitSystem(
            _world, portrait,
            uiSheet, FrameSource,         // the ornate wood frame (static)
            cowSheet, CowHeadSource,      // NPC face: the cow's head
            emoteSheet, EmoteCells);      // player face: emote cells

        UpdateSystem = CreateUpdateSystem(dialogueSystem, prompt, reactionSystem, portraitSystem);
        DrawSystem = CreateDrawSystem();
    }

    // ─── scene ────────────────────────────────────────────────────────────────

    private void CreateGround(ContentManager content)
    {
        var grass = content.Load<Texture2D>("SproutLands/Tilesets/Grass");
        // Plain grass cell — centre of the auto-tile block (fully grass-surrounded).
        var cell = new Rectangle(16, 16, 16, 16);
        var cols = (int)(BoundaryHalfWidth * 2 / TileSize);
        var rows = (int)(BoundaryHalfHeight * 2 / TileSize);
        for (var i = 0; i < cols; i++)
        for (var j = 0; j < rows; j++)
        {
            var pos = new Vector2(-BoundaryHalfWidth + i * TileSize, -BoundaryHalfHeight + j * TileSize);
            var tile = _world.CreateEntity();
            tile.Set(new TransformComponent(pos));
            tile.Set(new SpriteInfoComponent
            {
                SpriteSheet = grass,
                Source = cell,
                Size = new Vector2(TileSize, TileSize),
                Color = Color.White,
                Target = RenderTargetID.Main,
                LayerDepth = 0.10f,
                Origin = Vector2.Zero,
            });
            tile.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
            tile.Set<VisibleComponent>();
        }
    }

    private void CreateBoundary()
    {
        var bounds = new Rectangle(
            -(int)BoundaryHalfWidth, -(int)BoundaryHalfHeight,
            (int)BoundaryHalfWidth * 2, (int)BoundaryHalfHeight * 2);

        var boundary = _world.CreateEntity();
        boundary.Set(new TransformComponent(Vector2.Zero));
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.2f };
        draw.SetMeshData(new RectangleOutlineMeshGenerator(bounds, thickness: 2f, color: SproutPalette.TextLight));
        boundary.Set(draw);
        boundary.Set<VisibleComponent>();
    }

    /// Returns the player's above-head reaction-mark child (hidden until a `<<react>>` command).
    private Entity CreatePlayer(ContentManager content, Texture2D iconSheet)
    {
        var sheet = content.Load<Texture2D>("SproutLands/Characters/Basic Charakter Spritesheet");
        _player = _world.CreateEntity();
        _player.Set(new EntityInfoComponent("Player", "DialogueDemoPlayer"));
        _player.Set(new TransformComponent(PlayerSpawn));
        _player.Set(new PlayerTag());
        _player.Set(new SpriteInfoComponent
        {
            SpriteSheet = sheet,
            Source = new Rectangle(0, 0, PlayerFrame, PlayerFrame),
            Size = new Vector2(PlayerRenderSize, PlayerRenderSize),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.50f,
            Origin = new Vector2(PlayerFrame / 2f, PlayerFrame), // feet at the transform position
        });
        _player.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        _player.Set<VisibleComponent>();

        return CreateReactionChild(_player, iconSheet, new Vector2(0, -PlayerRenderSize - 18));
    }

    /// Returns (cow's reaction-mark child, "E to talk" prompt child).
    private (Entity mark, Entity prompt) CreateNpc(Texture2D sheet, Texture2D iconSheet)
    {
        _npc = _world.CreateEntity();
        _npc.Set(new EntityInfoComponent("NPC", "DialogueDemoCow"));
        _npc.Set(new TransformComponent(NpcPosition));
        _npc.Set(new SpriteInfoComponent
        {
            SpriteSheet = sheet,
            Source = new Rectangle(0, 0, CowFrame, CowFrame),
            Size = new Vector2(CowRenderSize, CowRenderSize),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.50f,
            Origin = new Vector2(CowFrame / 2f, CowFrame), // feet at the transform position
        });
        _npc.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        _npc.Set<VisibleComponent>();

        var mark = CreateReactionChild(_npc, iconSheet, new Vector2(0, -CowRenderSize - 16));

        // "E to talk" prompt centred just above the cow's head — shown only when in range.
        const float promptScale = 0.26f;
        const string promptText = "E to talk";
        var measured = _font.MeasureString(promptText);
        var prompt = _world.CreateEntity();
        prompt.Set(new EntityInfoComponent("DialogueDemo", "InteractPrompt"));
        prompt.Set(new TransformComponent(new Vector2(-measured.Width * promptScale / 2f, -CowRenderSize - 24f)));
        prompt.SetParent(_npc);
        prompt.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.63f,
            TextContent = promptText,
            Font = _font,
            Color = SproutPalette.TextSelected,
            Scale = promptScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        prompt.Set(new DrawComponent { Type = DrawElementType.Text, Target = RenderTargetID.Main });
        // No VisibleComponent yet — NpcInteractionSystem toggles it.

        return (mark, prompt);
    }

    /// An above-head reaction-mark sprite (from all_icons), child of a character on the Main
    /// target. Hidden until a `<<react>>` command shows it; Main culls by VisibleComponent so
    /// show/hide is just toggling that tag.
    private Entity CreateReactionChild(Entity parent, Texture2D iconSheet, Vector2 localOffset)
    {
        const float markSize = 30f;
        var mark = _world.CreateEntity();
        mark.Set(new EntityInfoComponent("DialogueDemo", "Reaction"));
        mark.Set(new TransformComponent(localOffset));
        mark.SetParent(parent);
        mark.Set(new SpriteInfoComponent
        {
            SpriteSheet = iconSheet,
            Source = SproutIcons.Exclamation,
            Size = new Vector2(markSize, markSize),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.62f,
            Origin = new Vector2(SproutIcons.Cell / 2f, SproutIcons.Cell / 2f),
        });
        mark.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        // No VisibleComponent yet — ReactionMarkSystem toggles it on a <<react>> command.
        return mark;
    }

    /// Builds a nine-patch (corner = <paramref name="corner"/> px) from a finished panel sprite
    /// at <paramref name="r"/> on a sheet: corners kept at source size, edges + centre sampled as
    /// 1px strips and stretched. Lets the wide box/balloon panels scale without warping corners.
    private static NinePatchInfo Panel9(Rectangle r, int corner)
    {
        int x = r.X, y = r.Y, w = r.Width, h = r.Height, c = corner;
        return new NinePatchInfo(
            c,
            new Rectangle(x, y, c, c),                 // top-left
            new Rectangle(x + c, y, 1, c),             // top (stretched horizontally)
            new Rectangle(x + w - c, y, c, c),         // top-right
            new Rectangle(x, y + c, c, 1),             // left (stretched vertically)
            new Rectangle(x + c, y + c, 1, 1),         // centre (stretched both ways)
            new Rectangle(x + w - c, y + c, c, 1),     // right
            new Rectangle(x, y + h - c, c, c),         // bottom-left
            new Rectangle(x + c, y + h - c, 1, c),     // bottom
            new Rectangle(x + w - c, y + h - c, c, c));// bottom-right
    }

    /// Creates the single in-box emote portrait — an ornate wood frame (basic_pack) with the
    /// speaker's face inside — centred in the dialogue box's left gutter (see
    /// DialogueSystem.PortraitGutterBounds). Both start hidden (textures cleared);
    /// DialoguePortraitSystem fills them on an &lt;&lt;emote&gt;&gt; command and clears them on dialogue end.
    private PortraitSlot CreatePortraitSlot(Rectangle gutter, Texture2D uiSheet)
    {
        var frameTopLeft = new Vector2(
            gutter.X + (gutter.Width - FrameRenderSize) / 2f,
            gutter.Y + (gutter.Height - FrameRenderSize) / 2f);
        var faceOffset = (FrameRenderSize - FaceRenderSize) / 2f;

        // Wood frame — a sprite on UI. Hidden by nulling its sheet + texture (see DialoguePortraitSystem).
        var frame = _world.CreateEntity();
        frame.Set(new EntityInfoComponent("DialogueDemo", "PortraitFrame"));
        frame.Set(new TransformComponent(frameTopLeft));
        frame.Set(new SpriteInfoComponent
        {
            SpriteSheet = null,                 // shown on the first <<emote>>
            Source = FrameSource,
            Size = new Vector2(FrameRenderSize, FrameRenderSize),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = 0.92f,
            Origin = Vector2.Zero,
        });
        frame.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.UI });
        frame.Set<VisibleComponent>();

        // Speaker face — a sprite on UI, drawn on top of the frame window.
        var face = _world.CreateEntity();
        face.Set(new EntityInfoComponent("DialogueDemo", "PortraitFace"));
        face.Set(new TransformComponent(frameTopLeft + new Vector2(faceOffset, faceOffset)));
        face.Set(new SpriteInfoComponent
        {
            SpriteSheet = null, // hidden until a portrait is shown
            Source = EmoteCells["happy"],
            Size = new Vector2(FaceRenderSize, FaceRenderSize),
            Color = Color.White,
            Target = RenderTargetID.UI,
            LayerDepth = 0.93f,
            Origin = Vector2.Zero,
        });
        face.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.UI });
        face.Set<VisibleComponent>();

        return new PortraitSlot(frame, face);
    }

    // ─── HUD ────────────────────────────────────────────────────────────────

    private void BuildHud(ContentManager content)
    {
        var squareButtons = content.Load<Texture2D>("SproutLands/Buttons/square_26x26");

        DemoHeader.Build(
            _world, _viewportManager, _font, squareButtons,
            title: "dialogue",
            descriptionLines: new[]
            {
                "Walk up to the cow with the WASD keys, then press E to talk.",
                "Use the up and down arrow keys to choose a reply, E to pick it.",
                "Portraits show the speaker in the box; reaction marks pop overhead.",
            });

        BuildSidebar(squareButtons);
    }

    private void BuildSidebar(Texture2D squareButtons)
    {
        var capStyle = new KeyCapStyle
        {
            SpriteSheet = squareButtons,
            DefaultSource = SproutSquareButtons.CreamLight,
            HoverSource = SproutSquareButtons.CreamDark,
            ActiveSource = SproutSquareButtons.TanDark,
            CapPixels = 42,
            CapLabelScale = 0.22f,
            CapLabelColor = SproutPalette.WarmBrown,
        };
        var rowStyle = new KeyRowStyle
        {
            LabelColor = SproutPalette.TextLight,
            HoverColor = SproutPalette.TextHover,
            ActiveColor = SproutPalette.TextSelected,
            LabelScale = 0.18f,
            Gap = 10f,
            BackgroundColor = SproutPalette.DarkBgSecondary,
            HoverBackgroundColor = SproutPalette.DarkBgSecondary,
            ActiveBackgroundColor = SproutPalette.DarkBgSecondary,
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

    // ─── pipeline ────────────────────────────────────────────────────────────

    private SequentialSystem<GameState> CreateUpdateSystem(
        DialogueSystem dialogueSystem, Entity prompt,
        ReactionMarkSystem reactionSystem, DialoguePortraitSystem portraitSystem)
    {
        return new SequentialSystem<GameState>(
            new DialogueDemoInputSystem(_interact, _up, _down),  // feed interact/up/down each frame
            new CursorInputSystem(_world),
            new IntrinsicSizingSystem(_world),
            new AutoLayoutSystem(_world, _viewportManager),
            new DemoButtonInteractionSystem(_world),
            new DemoIconRecolorSystem(_world),
            new PlayerMovementSystem(_world, BoundaryHalfWidth, BoundaryHalfHeight, PlayerSpeed,
                PlayerFrame, PlayerRenderSize / 2f, PlayerRenderSize, () => _dialogueActive),
            new NpcInteractionSystem(_world, _player, _npc, prompt, _interact, InteractRange, () => _dialogueActive),
            dialogueSystem,
            reactionSystem,
            portraitSystem,
            new TextUpdateSystem(_world),                        // advance the reveal animation
            new DialogueDemoShortcutSystem(this),
            new HierarchySystem(_world),
            new CursorPositionSystem(_world, _camera, _viewportManager),
            new CursorDrawPrepSystem(_world));
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        return new SequentialSystem<GameState>(
            new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false),
            new TextPrepSystem(_world, pixelPerfectRendering: false),
            new MeshPrepSystem(_world),
            new ButtonMeshPrepSystem(_world),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.UI, _renderTargets[RenderTargetID.UI]),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]),
            new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, new[]
            {
                RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
                RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
                RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
            }));
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── emote frames ──────────────────────────────────────────────────────────

    private const int EmoteFrame = 32; // emotes sheet: 32×32 cells (160×480 → 5×15)

    private static Rectangle EmoteCell(int col, int row) =>
        new(col * EmoteFrame, row * EmoteFrame, EmoteFrame, EmoteFrame);

    /// Maps a Yarn `<<emote who kind>>` kind to a 32×32 cell on the emote sheet (the in-box
    /// portrait face). These are the left-column faces (all complete frames); re-point them
    /// after a visual check if a particular expression doesn't match its name.
    private static readonly Dictionary<string, Rectangle> EmoteCells = new()
    {
        ["happy"] = EmoteCell(0, 0),
        ["surprised"] = EmoteCell(0, 1),
        ["question"] = EmoteCell(0, 2),
        ["heart"] = EmoteCell(0, 3),
        ["sad"] = EmoteCell(0, 4),
    };

    /// Maps a Yarn `<<react who mark>>` mark to an icon on `all_icons` (the above-head
    /// reaction mark). Word tokens (not literal punctuation) keep the Yarn command lexer happy.
    private static readonly Dictionary<string, Rectangle> ReactionIcons = new()
    {
        ["exclaim"] = SproutIcons.Exclamation,
        ["question"] = SproutIcons.Question,
        ["star"] = SproutIcons.Star,
    };

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

/// WASD/arrow movement for the tagged player, clamped so the (rendered) sprite stays inside
/// the boundary. Frozen while a dialogue is active. Plays a subtle two-frame walk bob over the
/// source spritesheet (source frame ≠ on-screen render size).
[With(typeof(PlayerTag), typeof(TransformComponent), typeof(SpriteInfoComponent))]
public sealed class PlayerMovementSystem : AEntitySetSystem<GameState>
{
    private readonly float _halfWidth;
    private readonly float _halfHeight;
    private readonly float _speed;
    private readonly int _sourceFrame;
    private readonly float _spriteHalfWidth;
    private readonly float _spriteHeight;
    private readonly Func<bool> _isFrozen;
    private float _animTimer;
    private int _frame;

    private const float BobInterval = 0.18f;

    public PlayerMovementSystem(World world, float halfWidth, float halfHeight, float speed,
        int sourceFrame, float spriteHalfWidth, float spriteHeight, Func<bool> isFrozen)
        : base(world)
    {
        _halfWidth = halfWidth;
        _halfHeight = halfHeight;
        _speed = speed;
        _sourceFrame = sourceFrame;
        _spriteHalfWidth = spriteHalfWidth;
        _spriteHeight = spriteHeight;
        _isFrozen = isFrozen;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var sprite = ref entity.Get<SpriteInfoComponent>();

        if (_isFrozen())
        {
            _frame = 0;
            _animTimer = 0f;
            sprite.Source = new Rectangle(0, 0, _sourceFrame, _sourceFrame);
            return;
        }

        var keyboard = Keyboard.GetState();
        var dir = Vector2.Zero;
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left)) dir.X -= 1f;
        if (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right)) dir.X += 1f;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up)) dir.Y -= 1f;
        if (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)) dir.Y += 1f;

        var transform = entity.Get<TransformComponent>();

        if (dir != Vector2.Zero)
        {
            dir.Normalize();
            var next = transform.Position + dir * _speed * state.Time;
            // Feet anchored at the position; keep the whole rendered sprite inside the boundary.
            next.X = MathHelper.Clamp(next.X, -_halfWidth + _spriteHalfWidth, _halfWidth - _spriteHalfWidth);
            next.Y = MathHelper.Clamp(next.Y, -_halfHeight + _spriteHeight, _halfHeight);
            transform.Position = next;

            _animTimer += state.Time;
            if (_animTimer >= BobInterval)
            {
                _animTimer = 0f;
                _frame ^= 1; // toggle between the first two front-facing frames
            }
        }
        else
        {
            _frame = 0;
            _animTimer = 0f;
        }

        sprite.Source = new Rectangle(_frame * _sourceFrame, 0, _sourceFrame, _sourceFrame);
    }
}

/// Shows a "press E" prompt when the player is near the NPC and, on the interact edge,
/// publishes a <see cref="DialogueStartMessage"/>. Suppressed while a dialogue is active.
public sealed class NpcInteractionSystem : ISystem<GameState>
{
    private readonly Entity _player;
    private readonly Entity _npc;
    private readonly Entity _prompt;
    private readonly AInputState _interact;
    private readonly float _rangeSq;
    private readonly Func<bool> _dialogueActive;
    private readonly World _world;

    public bool IsEnabled { get; set; } = true;

    public NpcInteractionSystem(World world, Entity player, Entity npc, Entity prompt,
        AInputState interact, float range, Func<bool> dialogueActive)
    {
        _world = world;
        _player = player;
        _npc = npc;
        _prompt = prompt;
        _interact = interact;
        _rangeSq = range * range;
        _dialogueActive = dialogueActive;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled || !_player.IsAlive || !_npc.IsAlive) return;

        if (_dialogueActive())
        {
            SetPromptVisible(false);
            return;
        }

        var playerPos = _player.Get<TransformComponent>().Position;
        var npcPos = _npc.Get<TransformComponent>().Position;
        var inRange = Vector2.DistanceSquared(playerPos, npcPos) <= _rangeSq;

        SetPromptVisible(inRange);

        if (inRange && _interact.JustPressed())
            _world.Publish(new DialogueStartMessage(_npc, "Start"));
    }

    private void SetPromptVisible(bool visible)
    {
        if (!_prompt.IsAlive) return;
        var has = _prompt.Has<VisibleComponent>();
        if (visible && !has) _prompt.Set<VisibleComponent>();
        else if (!visible && has) _prompt.Remove<VisibleComponent>();
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// A pair of entities making up one in-box portrait: a wood-brown frame (mesh) and the emote
/// face (sprite) drawn on top of it.
public readonly record struct PortraitSlot(Entity Frame, Entity Face);

/// Above-head reaction marks. Subscribes to <see cref="DialogueCommandMessage"/>, reacts to
/// `react who mark` by showing an icon (from all_icons) above the player or cow on the Main
/// target, and auto-hides it after a delay. Clears on dialogue end.
public sealed class ReactionMarkSystem : ISystem<GameState>
{
    private readonly Entity _playerMark;
    private readonly Entity _npcMark;
    private readonly Dictionary<string, Rectangle> _icons;
    private readonly float _duration;

    public bool IsEnabled { get; set; } = true;

    public ReactionMarkSystem(World world, Entity playerMark, Entity npcMark,
        Dictionary<string, Rectangle> icons, float duration)
    {
        _playerMark = playerMark;
        _npcMark = npcMark;
        _icons = icons;
        _duration = duration;
        world.Subscribe(this);
    }

    [Subscribe]
    private void OnCommand(in DialogueCommandMessage msg)
    {
        var parts = msg.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "react") return;

        var target = parts[1] switch
        {
            "player" => _playerMark,
            "npc" => _npcMark,
            _ => default,
        };
        if (!target.IsAlive || !_icons.TryGetValue(parts[2], out var cell)) return;

        target.Set(new EmoteBubbleComponent { HideAt = float.NaN });
        if (!target.Has<VisibleComponent>()) target.Set<VisibleComponent>();
        ref var sprite = ref target.Get<SpriteInfoComponent>();
        sprite.Source = cell;
    }

    [Subscribe]
    private void OnDialogueActive(in DialogueActiveMessage msg)
    {
        if (!msg.IsActive)
        {
            Hide(_playerMark);
            Hide(_npcMark);
        }
    }

    public void Update(GameState state)
    {
        Expire(_playerMark, state.TotalTime);
        Expire(_npcMark, state.TotalTime);
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
/// `emote who kind` it shows the framed portrait of whoever is speaking in the single emote
/// frame (the box's left gutter). The NPC uses the cow's own head (a fixed crop — the cow has
/// no expression frames, so `kind` is ignored there); the player picks an emote face by `kind`.
/// The portrait persists until the next `emote` or the dialogue ends — it does not auto-expire
/// (unlike reaction marks). Frame + face live on the UI target, which always renders, so hiding
/// clears the drawable (the sprite's sheet + texture) rather than toggling VisibleComponent.
public sealed class DialoguePortraitSystem : ISystem<GameState>
{
    private readonly PortraitSlot _slot;
    private readonly Texture2D _frameSheet;
    private readonly Rectangle _frameSource;    // the ornate wood frame (static, shown on any emote)
    private readonly Texture2D _cowSheet;
    private readonly Rectangle _cowSource;      // the cow's head crop (same for every kind)
    private readonly Texture2D _emoteSheet;
    private readonly Dictionary<string, Rectangle> _emoteCells;

    public bool IsEnabled { get; set; } = true;

    public DialoguePortraitSystem(World world, PortraitSlot slot,
        Texture2D frameSheet, Rectangle frameSource,
        Texture2D cowSheet, Rectangle cowSource,
        Texture2D emoteSheet, Dictionary<string, Rectangle> emoteCells)
    {
        _slot = slot;
        _frameSheet = frameSheet;
        _frameSource = frameSource;
        _cowSheet = cowSheet;
        _cowSource = cowSource;
        _emoteSheet = emoteSheet;
        _emoteCells = emoteCells;
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
                ShowFace(_cowSheet, _cowSource); // the cow's head, regardless of kind
                break;
            case "player":
                if (_emoteCells.TryGetValue(parts[2], out var cell)) ShowFace(_emoteSheet, cell);
                break;
        }
    }

    [Subscribe]
    private void OnDialogueActive(in DialogueActiveMessage msg)
    {
        if (!msg.IsActive)
        {
            ClearSprite(_slot.Frame);
            ClearSprite(_slot.Face);
        }
    }

    public void Update(GameState state) { }

    private void ShowFace(Texture2D sheet, Rectangle source)
    {
        // Show the (static) wood frame on the first emote, then swap in the speaker's face.
        if (_slot.Frame.IsAlive)
        {
            ref var frameSprite = ref _slot.Frame.Get<SpriteInfoComponent>();
            frameSprite.SpriteSheet = _frameSheet;
            frameSprite.Source = _frameSource;
        }
        if (_slot.Face.IsAlive)
        {
            ref var faceSprite = ref _slot.Face.Get<SpriteInfoComponent>();
            faceSprite.SpriteSheet = sheet;
            faceSprite.Source = source;
        }
    }

    private static void ClearSprite(Entity e)
    {
        if (!e.IsAlive) return;
        ref var sprite = ref e.Get<SpriteInfoComponent>();
        sprite.SpriteSheet = null;              // SpritePrep skips it
        e.Get<DrawComponent>().Texture = null;  // and don't draw the stale texture
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
            (up, Keys.Up),
            (down, Keys.Down),
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
