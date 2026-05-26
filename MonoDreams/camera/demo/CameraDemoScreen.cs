using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Camera;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demo.Camera;

/// Camera block demo. Move a red circle inside a boundary rectangle with WASD/arrows;
/// switch between five fixed camera targets (numbered 1–5, each shown as a hoverable
/// region in the world) and a follow mode (key 0). The "smooth lerp" toggle controls
/// the damping on <see cref="CameraFollowTargetComponent"/>: when on, the camera eases
/// toward its target (ball in follow mode, or the corresponding zone center in fixed
/// modes); when off, it snaps instantly.
public class CameraDemoScreen : IGameScreen
{
    private const float BoundaryHalfWidth = 380f;
    private const float BoundaryHalfHeight = 220f;
    private const float ZoneSize = 80f;
    private const float BallRadius = 20f;
    private const float MoveSpeed = 240f;

    private const float DampingSmooth = 5f;
    private const float DampingInstant = 100f;

    /// Keyboard-key 0..5 maps directly to enum index: 0 Follow, 1 TL, 2 TR,
    /// 3 BR, 4 BL, 5 Center. Reordering is intentional — clockwise around the
    /// boundary corners with Center last.
    public enum Mode { Follow, FixedTL, FixedTR, FixedBR, FixedBL, FixedCenter }

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;

    private ScreenController? _screenController;
    private Entity _ball;
    private Entity _cameraAnchor;
    private Entity _lerpToggle;
    private Entity _targetCross;
    private Mode _mode = Mode.Follow;
    private bool _lerpSmooth = true;

    private readonly Dictionary<Mode, Entity> _sidebarButtons = new();
    private readonly Dictionary<Mode, Entity> _worldZoneButtons = new();

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public CameraDemoScreen(GraphicsDevice graphicsDevice, ContentManager content, MonoDreams.Component.Camera camera,
        ViewportManager viewportManager, SpriteBatch spriteBatch)
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
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnButtonClicked);

        var cursorTextures = new Dictionary<CursorType, Texture2D>
        {
            [CursorType.Default] = content.Load<Texture2D>("Cursor/default"),
            [CursorType.Pointer] = content.Load<Texture2D>("Cursor/pointer"),
            [CursorType.Hand] = content.Load<Texture2D>("Cursor/hand"),
        };
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        _cameraAnchor = _world.CreateEntity();
        _cameraAnchor.Set(new TransformComponent(Vector2.Zero));

        CreateBoundary();
        CreateWorldZones();
        CreateBall();
        CreateTargetCross();
        CreateCameraCenterCross();
        BuildHud(content);

        SetMode(_mode);
    }

    // ─── public bridges for the keyboard system ───────────────────────────────

    public void SetMode(Mode mode)
    {
        _mode = mode;
        foreach (var (m, btn) in _sidebarButtons) UpdateActive(btn, m == mode);
        foreach (var (m, btn) in _worldZoneButtons) UpdateActive(btn, m == mode);

        if (mode == Mode.Follow)
        {
            if (_cameraAnchor.Has<CameraFollowTargetComponent>())
                _cameraAnchor.Remove<CameraFollowTargetComponent>();
            if (!_ball.Has<CameraFollowTargetComponent>())
                _ball.Set(new CameraFollowTargetComponent());
            ApplyDampingTo(_ball);
            ReparentTargetCross(_ball);
        }
        else
        {
            if (_ball.Has<CameraFollowTargetComponent>())
                _ball.Remove<CameraFollowTargetComponent>();
            var anchorTransform = _cameraAnchor.Get<TransformComponent>();
            anchorTransform.Position = ModeCameraTarget(mode);
            if (!_cameraAnchor.Has<CameraFollowTargetComponent>())
                _cameraAnchor.Set(new CameraFollowTargetComponent());
            ApplyDampingTo(_cameraAnchor);
            ReparentTargetCross(_cameraAnchor);

            if (!_lerpSmooth) _camera.Position = anchorTransform.Position;
        }
    }

    private void ReparentTargetCross(Entity newParent)
    {
        if (!_targetCross.IsAlive) return;
        // SetParent overwrites ChildOfComponent + transform.Parent in place;
        // we deliberately skip RemoveParent so the cross's LOCAL position stays
        // at (0,0). RemoveParent snapshots the current world position into the
        // local one, which would double the offset on the next reparent.
        _targetCross.SetParent(newParent);
        _targetCross.Get<TransformComponent>().Position = Vector2.Zero;
    }

    public void GoBackToLauncher() => _screenController?.LoadScreen(DemoScreens.Launcher);

    public void ToggleLerp()
    {
        if (!_lerpToggle.IsAlive || !_lerpToggle.Has<ToggleSwitchComponent>()) return;
        var sw = _lerpToggle.Get<ToggleSwitchComponent>();
        sw.On = !sw.On;
        _lerpSmooth = sw.On;
        _lerpToggle.Set(sw);

        if (_ball.Has<CameraFollowTargetComponent>()) ApplyDampingTo(_ball);
        if (_cameraAnchor.Has<CameraFollowTargetComponent>())
        {
            ApplyDampingTo(_cameraAnchor);
            if (!_lerpSmooth) _camera.Position = _cameraAnchor.Get<TransformComponent>().Position;
        }
    }

    private void ApplyDampingTo(Entity target)
    {
        if (!target.Has<CameraFollowTargetComponent>()) return;
        var ft = target.Get<CameraFollowTargetComponent>();
        ft.DampingX = _lerpSmooth ? DampingSmooth : DampingInstant;
        ft.DampingY = _lerpSmooth ? DampingSmooth : DampingInstant;
        ft.MaxDistanceX = 10000f;
        ft.MaxDistanceY = 10000f;
    }

    private static void UpdateActive(Entity entity, bool active)
    {
        if (!entity.IsAlive || !entity.Has<DemoButtonComponent>()) return;
        ref var b = ref entity.Get<DemoButtonComponent>();
        b.IsActive = active;
    }

    // Camera centers exactly on the zone the user clicked. Zone centers sit at the
    // corners offset by half the zone size so the zone sprite is fully on-screen.
    private static Vector2 ModeCameraTarget(Mode m)
    {
        var halfZone = ZoneSize / 2f;
        return m switch
        {
            Mode.FixedTL     => new Vector2(-BoundaryHalfWidth + halfZone, -BoundaryHalfHeight + halfZone),
            Mode.FixedTR     => new Vector2( BoundaryHalfWidth - halfZone, -BoundaryHalfHeight + halfZone),
            Mode.FixedCenter => Vector2.Zero,
            Mode.FixedBL     => new Vector2(-BoundaryHalfWidth + halfZone,  BoundaryHalfHeight - halfZone),
            Mode.FixedBR     => new Vector2( BoundaryHalfWidth - halfZone,  BoundaryHalfHeight - halfZone),
            _                => Vector2.Zero,
        };
    }

    private static Vector2 ZoneTopLeft(Mode m)
    {
        var half = ZoneSize / 2f;
        return m switch
        {
            Mode.FixedTL     => new Vector2(-BoundaryHalfWidth,             -BoundaryHalfHeight),
            Mode.FixedTR     => new Vector2( BoundaryHalfWidth - ZoneSize,  -BoundaryHalfHeight),
            Mode.FixedCenter => new Vector2(-half, -half),
            Mode.FixedBL     => new Vector2(-BoundaryHalfWidth,              BoundaryHalfHeight - ZoneSize),
            Mode.FixedBR     => new Vector2( BoundaryHalfWidth - ZoneSize,   BoundaryHalfHeight - ZoneSize),
            _ => Vector2.Zero,
        };
    }

    // ─── button click routing ────────────────────────────────────────────────

    private void OnButtonClicked(in DemoButtonClicked msg)
    {
        switch (msg.Id)
        {
            case DemoHeader.BackId: _screenController?.LoadScreen(DemoScreens.Launcher); break;
            case DemoHeader.ExitId: _screenController?.Game.Exit(); break;
            case "mode.follow": SetMode(Mode.Follow); break;
            case "mode.tl":     SetMode(Mode.FixedTL); break;
            case "mode.tr":     SetMode(Mode.FixedTR); break;
            case "mode.center": SetMode(Mode.FixedCenter); break;
            case "mode.bl":     SetMode(Mode.FixedBL); break;
            case "mode.br":     SetMode(Mode.FixedBR); break;
            case "toggle.lerp": ToggleLerp(); break;
        }
    }

    // ─── world entities ──────────────────────────────────────────────────────

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

    private void CreateWorldZones()
    {
        // Numbering matches the keyboard digits — 1 TL, 2 TR, 3 BR, 4 BL, 5 Center.
        CreateWorldZone(Mode.FixedTL,     "1", "mode.tl");
        CreateWorldZone(Mode.FixedTR,     "2", "mode.tr");
        CreateWorldZone(Mode.FixedBR,     "3", "mode.br");
        CreateWorldZone(Mode.FixedBL,     "4", "mode.bl");
        CreateWorldZone(Mode.FixedCenter, "5", "mode.center");
    }

    private void CreateWorldZone(Mode mode, string label, string id)
    {
        var topLeft = ZoneTopLeft(mode);
        var sharedTransform = new TransformComponent(topLeft);

        var container = _world.CreateEntity();
        container.Set(sharedTransform);

        const float labelScale = 0.50f;
        var measured = _font.MeasureString(label);
        var labelSize = new Vector2(measured.Width * labelScale, measured.Height * labelScale);
        var labelOffset = new Vector2((ZoneSize - labelSize.X) / 2f, (ZoneSize - labelSize.Y) / 2f);
        var labelEntity = _world.CreateEntity();
        labelEntity.Set(new TransformComponent(labelOffset));
        labelEntity.SetParent(container);
        labelEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.35f,
            TextContent = label,
            Font = _font,
            Color = SproutPalette.TextLight,
            Scale = labelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        labelEntity.Set<VisibleComponent>();

        var outline = _world.CreateEntity();
        outline.Set(sharedTransform);
        outline.Set(new SimpleButtonComponent
        {
            Size = new Vector2(ZoneSize, ZoneSize),
            LineThickness = 1.5f,
            Color = SproutPalette.TextLight,
            TextEntity = labelEntity,
            Target = RenderTargetID.Main,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = SproutPalette.TextLight,
            HoveredColor = SproutPalette.TextHover,
            ActiveColor = SproutPalette.TextSelected,
        });
        outline.Set<VisibleComponent>();
        _worldZoneButtons[mode] = outline;
    }

    private void CreateBall()
    {
        _ball = _world.CreateEntity();
        _ball.Set(new TransformComponent(Vector2.Zero));
        _ball.Set(new PlayerBallTag());
        // 0.97 puts the ball above the world-zone button outlines (those get
        // LayerDepth=0.95 from ButtonMeshPrepSystem) so the ball doesn't get
        // clipped by a zone's outline as it crosses through.
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.97f };
        draw.SetMeshData(new CircleMeshGenerator(Vector2.Zero, BallRadius, SproutPalette.Crimson, segments: 32));
        _ball.Set(draw);
        _ball.Set<VisibleComponent>();

        // Clickable hit-area for the ball — child entity offset to the ball's
        // top-left so the bounds-based hit test wraps the circle. Clicking the
        // ball switches to follow mode.
        var hitArea = _world.CreateEntity();
        hitArea.Set(new TransformComponent(new Vector2(-BallRadius, -BallRadius)));
        hitArea.SetParent(_ball);
        hitArea.Set(new SimpleButtonComponent
        {
            Size = new Vector2(BallRadius * 2, BallRadius * 2),
            LineThickness = 0f,
            Color = Color.Transparent,
            FillColor = Color.Transparent,
            TextEntity = null,
            Target = RenderTargetID.Main,
        });
        hitArea.Set(new DemoButtonComponent
        {
            Id = "mode.follow",
            DefaultColor = Color.Transparent,
            HoveredColor = Color.Transparent,
            ActiveColor = Color.Transparent,
        });
        hitArea.Set<VisibleComponent>();
    }

    /// Green cross marking where the camera *wants* to be. Parented to whichever
    /// entity currently has <see cref="CameraFollowTargetComponent"/> so it sits
    /// exactly at the target — when the camera lerps, the green cross stays put
    /// and the screen-center yellow cross drifts toward it.
    private void CreateTargetCross()
    {
        _targetCross = _world.CreateEntity();
        _targetCross.Set(new TransformComponent(Vector2.Zero));
        // 0.98 puts the cross above the ball (0.97) so the target marker is
        // never occluded when it sits on the ball in follow mode.
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.98f };
        draw.SetMeshData(CrossMesh(armPixels: 16, thicknessPixels: 3, color: Color.Black));
        _targetCross.Set(draw);
        _targetCross.Set<VisibleComponent>();
    }

    /// Yellow cross drawn on HUD at the screen center — marks the point the
    /// camera is currently looking at. Camera-independent (HUD is not affected
    /// by the view transform), so this stays put even as the world scrolls.
    private void CreateCameraCenterCross()
    {
        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(
            _viewportManager.VirtualWidth / 2f,
            _viewportManager.VirtualHeight / 2f)));
        var draw = new DrawComponent { Target = RenderTargetID.HUD, LayerDepth = 0.92f };
        draw.SetMeshData(CrossMesh(armPixels: 14, thicknessPixels: 3, color: Color.White));
        entity.Set(draw);
        entity.Set<VisibleComponent>();
    }

    private static CompositeMeshGenerator CrossMesh(int armPixels, int thicknessPixels, Color color)
    {
        var halfT = thicknessPixels / 2;
        return new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(
                new Rectangle(-armPixels, -halfT, armPixels * 2, thicknessPixels), color))
            .Add(new FilledRectangleMeshGenerator(
                new Rectangle(-halfT, -armPixels, thicknessPixels, armPixels * 2), color));
    }

    // ─── HUD ────────────────────────────────────────────────────────────────

    private void BuildHud(ContentManager content)
    {
        var squareButtons = content.Load<Texture2D>("SproutLands/Buttons/square_26x26");
        var settingsSheet = content.Load<Texture2D>("SproutLands/Buttons/settings_buttons");

        DemoHeader.Build(
            _world, _viewportManager, _font, squareButtons,
            title: "camera",
            descriptionLines: new[]
            {
                "Use keyboard keys 0 to 5 to switch the camera target",
                "and the L key to toggle camera lerp.",
                "Use the WASD keys to control the red dot.",
            });

        BuildSidebar(squareButtons, settingsSheet);
    }

    private void BuildSidebar(Texture2D squareButtons, Texture2D settingsSheet)
    {
        var capStyle = new KeyCapStyle
        {
            SpriteSheet = squareButtons,
            DefaultSource = SproutSquareButtons.CreamLight,
            HoverSource   = SproutSquareButtons.CreamDark,
            ActiveSource  = SproutSquareButtons.TanDark,
            // 42 matches the lerp toggle pill width so all sidebar elements
            // line up on a shared left edge.
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

        // Order: 0 follow, 1 TL, 2 TR, 3 BR, 4 BL, 5 center.
        var follow = Row("mode.follow", "0", "follow red dot");
        var tl     = Row("mode.tl",     "1", "top-left");
        var tr     = Row("mode.tr",     "2", "top-right");
        var br     = Row("mode.br",     "3", "bottom-right");
        var bl     = Row("mode.bl",     "4", "bottom-left");
        var center = Row("mode.center", "5", "center");

        // Toggle pill native is 28x18 (aspect ~1.56). Draw at 42x27 = native × 1.5
        // so the aspect ratio is preserved and the pill scales to match the cap chip.
        var lerp = _world.CreateToggleRow(
            id: "toggle.lerp",
            rowLabel: "smooth lerp",
            font: _font,
            toggleSheet: settingsSheet,
            offSource: SproutSettings.ToggleOff,
            onSource: SproutSettings.ToggleOn,
            initiallyOn: _lerpSmooth,
            toggleSize: new Vector2(42, 27),
            row: rowStyle,
            layerDepth: 0.96f);

        _sidebarButtons[Mode.Follow]      = follow.Outline;
        _sidebarButtons[Mode.FixedTL]     = tl.Outline;
        _sidebarButtons[Mode.FixedTR]     = tr.Outline;
        _sidebarButtons[Mode.FixedBR]     = br.Outline;
        _sidebarButtons[Mode.FixedBL]     = bl.Outline;
        _sidebarButtons[Mode.FixedCenter] = center.Outline;
        _lerpToggle = lerp.Outline;

        const float rowGap = 6f;       // gap BETWEEN per-row backgrounds
        const float groupGap = 16f;    // larger gap before the lerp toggle group

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopLeft, RenderTargetID.HUD)
            .Direction(LayoutDirection.Vertical)
            .Gap(rowGap)
            // Screen-root stacks roots vertically, so the sidebar already lands
            // below the header automatically — just add a small breathing pad.
            .Padding(20 /* top */, 12 /* right */, 12 /* bottom */, 12 /* left */)
            .AlignCross(CrossAxisAlignment.Start)
            .AddSlot(slot => slot.Attach(follow.Container).MeasureWith(_ => follow.Size))
            .AddSlot(slot => slot.Attach(tl.Container).MeasureWith(_ => tl.Size))
            .AddSlot(slot => slot.Attach(tr.Container).MeasureWith(_ => tr.Size))
            .AddSlot(slot => slot.Attach(br.Container).MeasureWith(_ => br.Size))
            .AddSlot(slot => slot.Attach(bl.Container).MeasureWith(_ => bl.Size))
            .AddSlot(slot => slot.Attach(center.Container).MeasureWith(_ => center.Size))
            .AddSlot(slot => slot.Attach(_world.CreateEntity()).MeasureWith(_ => new Vector2(0, groupGap - rowGap)))
            .AddSlot(slot => slot.Attach(lerp.Container).MeasureWith(_ => lerp.Size))
            .Build();
    }

    // ─── pipeline ────────────────────────────────────────────────────────────

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        return new SequentialSystem<GameState>(
            new CursorInputSystem(_world),
            new IntrinsicSizingSystem(_world),
            new AutoLayoutSystem(_world, _viewportManager),
            new DemoButtonInteractionSystem(_world),
            new DemoIconRecolorSystem(_world),
            new ToggleSwitchSystem(_world),
            new PlayerBallMovementSystem(_world, BoundaryHalfWidth, BoundaryHalfHeight, BallRadius, MoveSpeed),
            new CameraDemoInputSystem(this),
            new CameraFollowSystem(_world, _camera),
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
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _camera, _renderTargets, _world),
            new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, _camera, _renderTargets));
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// Tag identifying the player-controlled circle in the camera demo.
public struct PlayerBallTag { }

/// Reads keyboard input (WASD + arrows) and translates the tagged player entity,
/// clamping its position so the ball stays inside the boundary rectangle.
[With(typeof(PlayerBallTag), typeof(TransformComponent))]
public class PlayerBallMovementSystem : AEntitySetSystem<GameState>
{
    private readonly float _halfWidth;
    private readonly float _halfHeight;
    private readonly float _radius;
    private readonly float _speed;

    public PlayerBallMovementSystem(World world, float halfWidth, float halfHeight, float radius, float speed)
        : base(world)
    {
        _halfWidth = halfWidth;
        _halfHeight = halfHeight;
        _radius = radius;
        _speed = speed;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        var keyboard = Keyboard.GetState();
        var dir = Vector2.Zero;
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left))  dir.X -= 1f;
        if (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right)) dir.X += 1f;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up))    dir.Y -= 1f;
        if (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down))  dir.Y += 1f;
        if (dir != Vector2.Zero) dir.Normalize();

        var transform = entity.Get<TransformComponent>();
        var next = transform.Position + dir * _speed * state.Time;
        next.X = MathHelper.Clamp(next.X, -_halfWidth + _radius, _halfWidth - _radius);
        next.Y = MathHelper.Clamp(next.Y, -_halfHeight + _radius, _halfHeight - _radius);
        transform.Position = next;
    }
}

/// Polls the keyboard for camera-mode shortcuts (0–5, L) and forwards them
/// to the owning <see cref="CameraDemoScreen"/>. Edge-triggered: each key press
/// fires once per release-then-press cycle.
public class CameraDemoInputSystem : ISystem<GameState>
{
    private readonly CameraDemoScreen _screen;
    private KeyboardState _previous;
    public bool IsEnabled { get; set; } = true;

    public CameraDemoInputSystem(CameraDemoScreen screen)
    {
        _screen = screen;
        _previous = Keyboard.GetState();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        var current = Keyboard.GetState();
        bool Pressed(Keys k) => current.IsKeyDown(k) && !_previous.IsKeyDown(k);

        if (Pressed(Keys.D0) || Pressed(Keys.NumPad0)) _screen.SetMode(CameraDemoScreen.Mode.Follow);
        if (Pressed(Keys.D1) || Pressed(Keys.NumPad1)) _screen.SetMode(CameraDemoScreen.Mode.FixedTL);
        if (Pressed(Keys.D2) || Pressed(Keys.NumPad2)) _screen.SetMode(CameraDemoScreen.Mode.FixedTR);
        if (Pressed(Keys.D3) || Pressed(Keys.NumPad3)) _screen.SetMode(CameraDemoScreen.Mode.FixedBR);
        if (Pressed(Keys.D4) || Pressed(Keys.NumPad4)) _screen.SetMode(CameraDemoScreen.Mode.FixedBL);
        if (Pressed(Keys.D5) || Pressed(Keys.NumPad5)) _screen.SetMode(CameraDemoScreen.Mode.FixedCenter);
        if (Pressed(Keys.L)) _screen.ToggleLerp();
        if (Pressed(Keys.Escape)) _screen.GoBackToLauncher();

        _previous = current;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
