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

/// Camera module demo. Move a red circle inside a boundary rectangle with WASD/arrows;
/// switch between five fixed camera targets (numbered 1–5, each shown as a hoverable
/// region in the world) and a follow mode (key 0). The "smooth lerp" toggle controls
/// the damping on <see cref="CameraFollowTargetComponent"/>: when on, the camera eases
/// toward its target (ball in follow mode, or the corresponding zone center in fixed
/// modes); when off, it snaps instantly.
///
/// In follow mode the camera also zooms out a little as its center lags behind the
/// red dot — see <see cref="CameraLagZoomSystem"/>. The more the dot outruns the
/// camera (fast moves, or pushing into the camera-bounds edges), the wider the view.
///
/// Also in follow mode, when the red dot's center enters one of the four corner
/// zones the camera stops tracking the dot and instead prioritizes a fixed point a
/// little past that corner (a light-blue crosshair marks it) — see
/// <see cref="CornerPrioritySystem"/>. Leaving the corner zone hands tracking back
/// to the dot. This is the documented swap pattern: a second follow target whose
/// <see cref="CameraFollowTargetComponent.IsActive"/> flag we toggle against the ball's.
///
/// Finally, two hollow red squares flank the center zone (left and right, inside the
/// dashed camera-bounds rectangle). Driving the red dot into one fires a brief "hit" in any
/// camera mode: the square blinks yellow, and the camera takes a quick, decaying jolt on
/// top of its current motion — a positional <b>shake</b> from the right square, a small
/// <b>rotation</b> wobble from the left one — see <see cref="CameraHitSystem"/>. Both jolts
/// are layered on top of the resolved camera transform (last-write-wins, the documented
/// composable pattern), so they never fight <see cref="CameraFollowSystem"/>'s ownership of
/// the camera position.
public class CameraDemoScreen : IGameScreen
{
    private const float BoundaryHalfWidth = 380f;
    private const float BoundaryHalfHeight = 220f;
    // Camera-movement bounds in follow mode — a little smaller than the boundary,
    // so the red dot can push past the camera's reach toward the screen edges.
    private const float CameraBoundsHalfWidth = 280f;
    private const float CameraBoundsHalfHeight = 140f;
    private const float ZoneSize = 80f;
    private const float BallRadius = 20f;
    private const float MoveSpeed = 240f;
    // How far past the boundary corner the camera overshoots when the red dot
    // enters a corner zone — pushed well past the boundary on both axes.
    private const float CornerOverscan = 150f;

    // Two little hollow red "hit" squares flanking the center, kept inside the dashed
    // camera-bounds rect (±CameraBounds). Driving the dot into one fires a camera jolt.
    private const float HitSquareSize = 50f;
    private const float HitSquareOffsetX = 150f;
    // How long a struck square flashes yellow before reverting to red.
    private const float HitBlinkDuration = 0.18f;

    private const float DampingSmooth = 5f;
    private const float DampingInstant = 100f;

    // Minimap: a second camera fixed at the region center, rendered to a small box
    // anchored to the bottom-right of the HUD. The box keeps the 16:9 virtual aspect
    // so the downscaled world view isn't distorted.
    private const int MinimapWidth = 320;
    private const int MinimapHeight = 180;
    private const int MinimapMargin = 24;
    // Fraction of the tightest fit, leaving a margin around the region in the minimap.
    private const float MinimapFitPadding = 0.85f;

    /// Keyboard-key 0..5 maps directly to enum index: 0 Follow, 1 TL, 2 TR,
    /// 3 BR, 4 BL, 5 Center. Reordering is intentional — clockwise around the
    /// boundary corners with Center last.
    public enum Mode { Follow, FixedTL, FixedTR, FixedBR, FixedBL, FixedCenter }

    /// Which flanking hit square (if any) the red dot currently sits in — <c>None</c> when
    /// the dot is clear of both. <see cref="CameraHitSystem"/> reads this to fire shake
    /// (Right) / rotate (Left).
    public enum HitZone { None, Left, Right }

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;
    // Second camera + its own target for the minimap (see MinimapWidth/Height).
    private readonly MonoDreams.Component.Camera _minimapCamera;
    private readonly RenderTarget2D _minimapTarget;

    private ScreenController? _screenController;
    private Entity _ball;
    private Entity _cameraAnchor;
    private Entity _lerpToggle;
    private Entity _targetCross;
    private Entity _cameraBoundsRect;
    // Free-following anchor the camera prioritizes while the red dot sits in a
    // corner zone; carries the light-blue corner crosshair as a child.
    private Entity _cornerAnchor;
    private Entity _cornerCross;
    // While corner priority is engaged, the distance from the prioritized point to
    // the corner zone's center; null when disengaged. Drives the corner zoom-out.
    private float? _cornerZoomDistance;
    // The two flanking hit squares and their remaining yellow-flash time (seconds).
    private Entity _leftHitSquare;
    private Entity _rightHitSquare;
    private float _leftBlink;
    private float _rightBlink;
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

        // Minimap camera: same virtual resolution as the main camera (the mesh
        // projection is shared across render passes), fixed at the region center, and
        // zoomed so the whole boundary region fits with a margin. Never moved or zoomed.
        var fitZoom = Math.Min(
            viewportManager.VirtualWidth / (BoundaryHalfWidth * 2f),
            viewportManager.VirtualHeight / (BoundaryHalfHeight * 2f)) * MinimapFitPadding;
        _minimapCamera = new MonoDreams.Component.Camera(viewportManager.VirtualWidth, viewportManager.VirtualHeight)
        {
            Position = Vector2.Zero,
            Zoom = fitZoom,
        };
        _minimapTarget = new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight);

        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnButtonClicked);

        MonoDreams.Cursor.Cursor.CreateMesh(_world,
            ShapeBuilder.Arrow(26f, Color.Black, Color.White).Generate(), RenderTargetID.HUD);

        _cameraAnchor = _world.CreateEntity();
        _cameraAnchor.Set(new TransformComponent(Vector2.Zero));

        CreateBoundary();
        CreateCameraBoundsRect();
        CreateWorldZones();
        CreateHitSquares();
        CreateBall();
        CreateTargetCross();
        CreateCornerPriorityAnchor();
        CreateCameraCenterCross();
        CreateMinimapFrame();
        BuildHud(content);

        SetMode(_mode);
    }

    // ─── public bridges for the keyboard system ───────────────────────────────

    public void SetMode(Mode mode)
    {
        _mode = mode;
        foreach (var (m, btn) in _sidebarButtons) UpdateActive(btn, m == mode);
        foreach (var (m, btn) in _worldZoneButtons) UpdateActive(btn, m == mode);

        // The dashed camera-bounds guide only makes sense while the camera tracks
        // the red dot — show it in follow mode, hide it in the fixed modes.
        SetCameraBoundsVisible(mode == Mode.Follow);

        if (mode == Mode.Follow)
        {
            if (_cameraAnchor.Has<CameraFollowTargetComponent>())
                _cameraAnchor.Remove<CameraFollowTargetComponent>();
            if (!_ball.Has<CameraFollowTargetComponent>())
                _ball.Set(new CameraFollowTargetComponent());
            ApplyDampingTo(_ball);
            _ball.Get<CameraFollowTargetComponent>().Bounds = CameraBounds();
            ReparentTargetCross(_ball);
        }
        else
        {
            // Leaving follow mode hands the camera to the fixed anchor — make sure
            // any corner-priority override is cleared so it can't fight the anchor.
            DisengageCornerPriority();
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
        if (_cornerAnchor.IsAlive && _cornerAnchor.Has<CameraFollowTargetComponent>()) ApplyDampingTo(_cornerAnchor);
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

    /// Per-frame corner-priority tick, driven by <see cref="CornerPrioritySystem"/>.
    /// Only engages in follow mode: when the red dot's center is inside a corner zone,
    /// the camera prioritizes the corner's overscan point (light-blue cross) by toggling
    /// the corner anchor active and the ball inactive; otherwise the ball stays the
    /// target. Runs after the ball moves and after mode switches, before the follow
    /// system, so at most one target is active when <see cref="CameraFollowSystem"/> reads.
    public void UpdateCornerPriority()
    {
        if (_mode != Mode.Follow)
        {
            DisengageCornerPriority();
            return;
        }

        var ballCenter = _ball.Get<TransformComponent>().Position;
        if (TryGetCornerZone(ballCenter, out var corner))
        {
            var point = CornerPriorityPoint(corner);
            _cornerAnchor.Get<TransformComponent>().Position = point;
            _cornerAnchor.Get<CameraFollowTargetComponent>().IsActive = true;
            if (_ball.Has<CameraFollowTargetComponent>())
                _ball.Get<CameraFollowTargetComponent>().IsActive = false;
            SetCornerCrossVisible(true);
            // How far the prioritized point sits beyond the corner zone's center —
            // CameraLagZoomSystem reads this to zoom out proportionally.
            _cornerZoomDistance = (point - CornerZoneCenter(corner)).Length();
        }
        else
        {
            DisengageCornerPriority();
        }
    }

    /// Distance the prioritized corner point sits from the corner zone's center while
    /// corner priority is engaged; null otherwise. Read by <see cref="CameraLagZoomSystem"/>.
    public float? CornerZoomDistance => _cornerZoomDistance;

    /// Which flanking hit square the red dot currently sits in, or <c>None</c>.
    /// <see cref="CameraHitSystem"/> reads this and fires on the rising edge (the dot
    /// entering a square), in any camera mode.
    public HitZone BallHitZone
    {
        get
        {
            if (!_ball.IsAlive) return HitZone.None;
            var center = _ball.Get<TransformComponent>().Position;
            if (HitSquareRect(-HitSquareOffsetX).Contains((int)center.X, (int)center.Y)) return HitZone.Left;
            if (HitSquareRect(HitSquareOffsetX).Contains((int)center.X, (int)center.Y)) return HitZone.Right;
            return HitZone.None;
        }
    }

    /// World-space rectangle of a hit square centred at <paramref name="centerX"/> on the
    /// horizontal axis (the squares straddle y = 0).
    private static Rectangle HitSquareRect(float centerX)
    {
        var half = (int)(HitSquareSize / 2f);
        return new Rectangle((int)centerX - half, -half, (int)HitSquareSize, (int)HitSquareSize);
    }

    /// Flashes the struck square yellow and (re)starts its blink timer. Called by
    /// <see cref="CameraHitSystem"/> on the frame the dot enters the square.
    public void BlinkHitSquare(HitZone zone)
    {
        switch (zone)
        {
            case HitZone.Left:  _leftBlink = HitBlinkDuration;  SetHitSquareColor(_leftHitSquare, DemoPalette.SoftYellow); break;
            case HitZone.Right: _rightBlink = HitBlinkDuration; SetHitSquareColor(_rightHitSquare, DemoPalette.SoftYellow); break;
        }
    }

    /// Ticks down the yellow flashes; when one expires its square reverts to red. Driven
    /// each frame by <see cref="CameraHitSystem"/>. Colors are only rewritten on the
    /// expiry transition, not every frame.
    public void UpdateHitSquareBlinks(float dt)
    {
        if (_leftBlink > 0f && (_leftBlink -= dt) <= 0f)
        {
            _leftBlink = 0f;
            SetHitSquareColor(_leftHitSquare, DemoPalette.Crimson);
        }
        if (_rightBlink > 0f && (_rightBlink -= dt) <= 0f)
        {
            _rightBlink = 0f;
            SetHitSquareColor(_rightHitSquare, DemoPalette.Crimson);
        }
    }

    /// Hands camera tracking back to the red dot and hides the corner crosshair.
    /// Safe to call in any mode — guards on component presence.
    private void DisengageCornerPriority()
    {
        if (_cornerAnchor.IsAlive && _cornerAnchor.Has<CameraFollowTargetComponent>())
            _cornerAnchor.Get<CameraFollowTargetComponent>().IsActive = false;
        if (_ball.IsAlive && _ball.Has<CameraFollowTargetComponent>())
            _ball.Get<CameraFollowTargetComponent>().IsActive = true;
        SetCornerCrossVisible(false);
        _cornerZoomDistance = null;
    }

    private void SetCornerCrossVisible(bool visible)
    {
        if (!_cornerCross.IsAlive) return;
        if (visible && !_cornerCross.Has<VisibleComponent>())
            _cornerCross.Set<VisibleComponent>();
        else if (!visible && _cornerCross.Has<VisibleComponent>())
            _cornerCross.Remove<VisibleComponent>();
    }

    // The four corner zones the dot can trigger priority from. Center (5) is excluded.
    private static readonly Mode[] CornerModes =
        { Mode.FixedTL, Mode.FixedTR, Mode.FixedBR, Mode.FixedBL };

    /// True when <paramref name="ballCenter"/> lies inside one of the four corner zones,
    /// yielding which corner it is.
    private static bool TryGetCornerZone(Vector2 ballCenter, out Mode corner)
    {
        foreach (var mode in CornerModes)
        {
            var topLeft = ZoneTopLeft(mode);
            var zone = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)ZoneSize, (int)ZoneSize);
            if (zone.Contains((int)ballCenter.X, (int)ballCenter.Y))
            {
                corner = mode;
                return true;
            }
        }
        corner = Mode.Follow;
        return false;
    }

    /// World-space center of a corner zone square.
    private static Vector2 CornerZoneCenter(Mode m) =>
        ZoneTopLeft(m) + new Vector2(ZoneSize / 2f, ZoneSize / 2f);

    /// The fixed point the camera looks at while the dot is in a corner zone: the
    /// boundary corner pushed out by <see cref="CornerOverscan"/> on both axes.
    private static Vector2 CornerPriorityPoint(Mode m) => m switch
    {
        Mode.FixedTL => new Vector2(-BoundaryHalfWidth - CornerOverscan, -BoundaryHalfHeight - CornerOverscan),
        Mode.FixedTR => new Vector2( BoundaryHalfWidth + CornerOverscan, -BoundaryHalfHeight - CornerOverscan),
        Mode.FixedBR => new Vector2( BoundaryHalfWidth + CornerOverscan,  BoundaryHalfHeight + CornerOverscan),
        Mode.FixedBL => new Vector2(-BoundaryHalfWidth - CornerOverscan,  BoundaryHalfHeight + CornerOverscan),
        _ => Vector2.Zero,
    };

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
        draw.SetMeshData(new RectangleOutlineMeshGenerator(bounds, thickness: 2f, color: DemoPalette.TextLight));
        boundary.Set(draw);
        boundary.Set<VisibleComponent>();
    }

    /// World-space rectangle the camera position is clamped to in follow mode.
    private static Rectangle CameraBounds() => new(
        -(int)CameraBoundsHalfWidth, -(int)CameraBoundsHalfHeight,
        (int)CameraBoundsHalfWidth * 2, (int)CameraBoundsHalfHeight * 2);

    /// Soft-yellow dashed guide showing how far the camera can travel in follow
    /// mode. Created once; its visibility is toggled by <see cref="SetMode"/> so it
    /// only appears while the red dot is the camera target.
    private void CreateCameraBoundsRect()
    {
        _cameraBoundsRect = _world.CreateEntity();
        _cameraBoundsRect.Set(new TransformComponent(Vector2.Zero));
        // 0.25 sits just above the solid boundary outline (0.2) and below the
        // world-zone outlines, ball, and crosses.
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.25f };
        draw.SetMeshData(new DashedRectangleOutlineMeshGenerator(
            CameraBounds(), thickness: 2f, color: DemoPalette.SoftYellow, dashLength: 14f, gapLength: 9f));
        _cameraBoundsRect.Set(draw);
        // VisibleComponent is added/removed by SetCameraBoundsVisible — no
        // CullingSystem runs in this demo, so the tag fully controls visibility.
    }

    private void SetCameraBoundsVisible(bool visible)
    {
        if (!_cameraBoundsRect.IsAlive) return;
        if (visible && !_cameraBoundsRect.Has<VisibleComponent>())
            _cameraBoundsRect.Set<VisibleComponent>();
        else if (!visible && _cameraBoundsRect.Has<VisibleComponent>())
            _cameraBoundsRect.Remove<VisibleComponent>();
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
            Color = DemoPalette.TextLight,
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
            Color = DemoPalette.TextLight,
            TextEntity = labelEntity,
            Target = RenderTargetID.Main,
        });
        outline.Set(new DemoButtonComponent
        {
            Id = id,
            DefaultColor = DemoPalette.TextLight,
            HoveredColor = DemoPalette.TextHover,
            ActiveColor = DemoPalette.TextSelected,
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
        draw.SetMeshData(new CircleMeshGenerator(Vector2.Zero, BallRadius, DemoPalette.Crimson, segments: 32));
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

    /// Two hollow red squares flanking the center zone — the camera "hit" triggers. Each
    /// sits at (±HitSquareOffsetX, 0), well inside the dashed camera-bounds rect. Their
    /// mesh color is swapped to yellow and back by the blink helpers when struck.
    private void CreateHitSquares()
    {
        _leftHitSquare = CreateHitSquare(new Vector2(-HitSquareOffsetX, 0f));
        _rightHitSquare = CreateHitSquare(new Vector2(HitSquareOffsetX, 0f));
    }

    private Entity CreateHitSquare(Vector2 center)
    {
        var square = _world.CreateEntity();
        square.Set(new TransformComponent(center));
        // 0.5 sits above the boundary (0.2) and dashed bounds (0.25), below the world-zone
        // outlines, ball, and crosses — the ball passes over the square cleanly.
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.5f };
        square.Set(draw);
        SetHitSquareColor(square, DemoPalette.Crimson);
        square.Set<VisibleComponent>();
        return square;
    }

    /// Rebuilds a hit square's outline mesh in the given color. Mesh vertices bake their
    /// color (MeshPrepSystem never regenerates them), so a flash means regenerating the
    /// outline — done only on the red↔yellow transitions, not per frame. The outline is
    /// centred on the entity's transform so its world rect is symmetric about the center.
    private static void SetHitSquareColor(Entity square, Color color)
    {
        if (!square.IsAlive || !square.Has<DrawComponent>()) return;
        var half = (int)(HitSquareSize / 2f);
        square.Get<DrawComponent>().SetMeshData(new RectangleOutlineMeshGenerator(
            new Rectangle(-half, -half, (int)HitSquareSize, (int)HitSquareSize), thickness: 2f, color: color));
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

    /// Free-following anchor for the corner-priority behavior, plus its light-blue
    /// crosshair child. The anchor carries a <see cref="CameraFollowTargetComponent"/>
    /// with no <c>Bounds</c> (so the camera can overshoot past the boundary corner) and
    /// starts inactive — <see cref="CornerPrioritySystem"/> activates it against the ball
    /// while the dot sits in a corner zone. The crosshair is parented to the anchor and
    /// its visibility is toggled with the override, so it always sits on the target point.
    private void CreateCornerPriorityAnchor()
    {
        _cornerAnchor = _world.CreateEntity();
        _cornerAnchor.Set(new TransformComponent(Vector2.Zero));
        _cornerAnchor.Set(new CameraFollowTargetComponent { IsActive = false });
        ApplyDampingTo(_cornerAnchor);

        _cornerCross = _world.CreateEntity();
        _cornerCross.Set(new TransformComponent(Vector2.Zero));
        _cornerCross.SetParent(_cornerAnchor);
        // 0.985 sits just above the green target cross (0.98) so the override marker
        // reads as the foremost crosshair while it's showing.
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.985f };
        draw.SetMeshData(CrossMesh(armPixels: 16, thicknessPixels: 3, color: DemoPalette.SkyBlue));
        _cornerCross.Set(draw);
        // VisibleComponent is added/removed by SetCornerCrossVisible — no CullingSystem
        // runs in this demo, so the tag fully controls visibility.
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

    /// Screen-space box (virtual coords) where the minimap is composited, anchored to
    /// the bottom-right of the HUD.
    private Rectangle MinimapDestination() => new(
        _viewportManager.VirtualWidth - MinimapMargin - MinimapWidth,
        _viewportManager.VirtualHeight - MinimapMargin - MinimapHeight,
        MinimapWidth, MinimapHeight);

    /// HUD chrome for the minimap: an opaque backdrop (so the minimap's transparent
    /// areas don't reveal the world behind the HUD) and a frame ring just outside the
    /// composited image. The minimap image itself is drawn by MasterRenderSystem's
    /// minimap composite on top of this backdrop, inside the frame.
    private void CreateMinimapFrame()
    {
        var dest = MinimapDestination();

        var bg = _world.CreateEntity();
        bg.Set(new TransformComponent(Vector2.Zero));
        // 0.90 keeps the backdrop below the screen-center cross (0.92) and cursor.
        var bgDraw = new DrawComponent { Target = RenderTargetID.HUD, LayerDepth = 0.90f };
        bgDraw.SetMeshData(new FilledRectangleMeshGenerator(dest, DemoPalette.DarkBgSecondary));
        bg.Set(bgDraw);
        bg.Set<VisibleComponent>();

        var frameRect = new Rectangle(dest.X - 2, dest.Y - 2, dest.Width + 4, dest.Height + 4);
        var frame = _world.CreateEntity();
        frame.Set(new TransformComponent(Vector2.Zero));
        var frameDraw = new DrawComponent { Target = RenderTargetID.HUD, LayerDepth = 0.91f };
        frameDraw.SetMeshData(new RectangleOutlineMeshGenerator(frameRect, thickness: 3f, color: DemoPalette.TextLight));
        frame.Set(frameDraw);
        frame.Set<VisibleComponent>();
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
        DemoHeader.Build(
            _world, _viewportManager, _font,
            title: "camera",
            descriptionLines: new[]
            {
                "Use keyboard keys 0 to 5 to switch the camera target",
                "and the L key to toggle camera lerp.",
                "Use the WASD keys to control the red dot.",
                "Hit the right red square to shake the camera, the left to rotate it.",
            });

        BuildSidebar();
    }

    private void BuildSidebar()
    {
        var capStyle = new KeyCapStyle
        {
            // 42 matches the checkbox box width so all sidebar elements line up on a
            // shared left edge.
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

        // Order: 0 follow, 1 TL, 2 TR, 3 BR, 4 BL, 5 center.
        var follow = Row("mode.follow", "0", "follow red dot");
        var tl     = Row("mode.tl",     "1", "top-left");
        var tr     = Row("mode.tr",     "2", "top-right");
        var br     = Row("mode.br",     "3", "bottom-right");
        var bl     = Row("mode.bl",     "4", "bottom-left");
        var center = Row("mode.center", "5", "center");

        // Checkbox box is 42×42 so it lines up with the 42px key caps above.
        var lerp = _world.CreateCheckboxRow(
            id: "toggle.lerp",
            rowLabel: "smooth lerp",
            font: _font,
            initiallyOn: _lerpSmooth,
            boxSize: 42f,
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
            new ToggleSwitchSystem(_world),
            new PlayerBallMovementSystem(_world, BoundaryHalfWidth, BoundaryHalfHeight, BallRadius, MoveSpeed),
            new CameraDemoInputSystem(this),
            // Runs after the ball moved and after mode switches, before the follow
            // system, so the right target is active when the camera resolves.
            new CornerPrioritySystem(this),
            new CameraFollowSystem(_world, _camera),
            // Runs after the follow system so it reads this frame's resolved
            // camera position when measuring the lag behind the red dot.
            new CameraLagZoomSystem(_world, _camera, this),
            new HierarchySystem(_world),
            new CursorPositionSystem(_world, _camera, _viewportManager),
            // Hit jolts run last so only rendering sees the offset/rotation — cursor and
            // hierarchy this frame already used the clean follow transform, and the system
            // peels off its own prior offset before re-applying, so the jolt never bleeds
            // into the follow path.
            new CameraHitSystem(_camera, this));
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        return new SequentialSystem<GameState>(
            new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false),
            new TextPrepSystem(_world, pixelPerfectRendering: false),
            new MeshPrepSystem(_world),
            new ButtonMeshPrepSystem(_world),
            // World view through the main camera, plus screen-space UI/HUD passes.
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.UI, _renderTargets[RenderTargetID.UI]),
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]),
            // Minimap: the same world (Main) entities through a second camera fixed at the
            // region center, rendered into its own target — just another render pass.
            new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
                RenderTargetID.Main, _minimapTarget, _minimapCamera),
            // Composite the targets onto the screen; the minimap lands in its bottom-right box.
            new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, new[]
            {
                RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
                RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
                RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
                RenderLayer.Overlay(_minimapTarget, MinimapDestination(), SamplerState.LinearClamp),
            }));
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _minimapTarget.Dispose();
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

/// Drives the screen's corner-priority override each frame (see
/// <see cref="CameraDemoScreen.UpdateCornerPriority"/>). The screen owns the entities
/// and mode state, so the per-frame tick lives there; this system just forwards it at
/// the right pipeline slot — after movement/mode switches, before the follow system.
public class CornerPrioritySystem : ISystem<GameState>
{
    private readonly CameraDemoScreen _screen;
    public bool IsEnabled { get; set; } = true;

    public CornerPrioritySystem(CameraDemoScreen screen) => _screen = screen;

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        _screen.UpdateCornerPriority();
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// Zooms the camera out a little, and eases the applied zoom toward that target so it
/// widens and recovers smoothly. Two sources drive the zoom-out:
///
/// 1. <b>Follow lag</b> — while the red dot is the active target, the world-space
///    distance between the ball and the resolved camera position. It grows when the
///    dot outruns the camera or pushes past the camera-bounds edges.
/// 2. <b>Corner priority</b> — while the camera prioritizes a corner point (the dot
///    sits in a corner zone), the distance from that point to the corner zone's center
///    (<see cref="CameraDemoScreen.CornerZoomDistance"/>). This is the dominant source
///    when engaged, so the view stays widened the whole time it looks past the corner.
///
/// Each distance maps linearly to a zoom reduction, capped at <see cref="MaxZoomOut"/>.
/// When neither applies (fixed modes, dot centered) the target zoom is 1. This only
/// writes <c>Camera.Zoom</c>, so it never competes with <see cref="CameraFollowSystem"/>'s
/// ownership of <c>Camera.Position</c>.
public class CameraLagZoomSystem : ISystem<GameState>
{
    // Follow-lag distance (in world units) at which the zoom-out reaches its full
    // MaxZoomOut. Sized for this demo's boundary/camera-bounds geometry.
    private const float MaxLag = 150f;
    // Corner-priority distance (blue point ↔ corner center) that maps to full MaxZoomOut.
    // Kept above the geometry's actual ~270px so the corner zoom stays proportional
    // (below the cap) and scales if the overscan changes.
    private const float CornerZoomReferenceDistance = 320f;
    // Largest zoom reduction; 0.18 means the camera widens to at most 0.82× — "a little".
    private const float MaxZoomOut = 0.18f;
    // Exponential easing rate for the applied zoom, matched to the smooth-lerp feel.
    private const float ZoomDamping = 6f;

    private readonly MonoDreams.Component.Camera _camera;
    private readonly CameraDemoScreen _screen;
    private readonly EntitySet _activeBallTargets;
    private float _currentZoom = 1f;

    public bool IsEnabled { get; set; } = true;

    public CameraLagZoomSystem(World world, MonoDreams.Component.Camera camera, CameraDemoScreen screen)
    {
        _camera = camera;
        _screen = screen;
        // The ball only carries CameraFollowTargetComponent while in follow mode, so
        // this set is non-empty exactly when the dot is the camera target.
        _activeBallTargets = world.GetEntities()
            .With<PlayerBallTag>()
            .With<CameraFollowTargetComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var targetZoom = 1f;
        if (_screen.CornerZoomDistance is { } cornerDistance)
        {
            // Corner priority owns the zoom while engaged: the camera looks at the fixed
            // corner point, so scale by that point's distance from the corner center.
            var t = MathHelper.Clamp(cornerDistance / CornerZoomReferenceDistance, 0f, 1f);
            targetZoom = 1f - t * MaxZoomOut;
        }
        else
        {
            // Otherwise scale by the active red-dot's follow lag.
            foreach (var entity in _activeBallTargets.GetEntities())
            {
                if (!entity.Get<CameraFollowTargetComponent>().IsActive) continue;
                var lag = (entity.Get<TransformComponent>().Position - _camera.Position).Length();
                var t = MathHelper.Clamp(lag / MaxLag, 0f, 1f);
                targetZoom = 1f - t * MaxZoomOut;
                break;
            }
        }

        var smooth = 1f - (float)Math.Exp(-ZoomDamping * state.Time);
        _currentZoom = MathHelper.Lerp(_currentZoom, targetZoom, smooth);
        _camera.Zoom = _currentZoom;
    }

    public void Dispose()
    {
        _activeBallTargets.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// Layers a short, rapid camera "hit" on top of the resolved camera transform to sell a
/// "the player just took a hit" moment. When the red dot enters one of the two flanking
/// hit squares — see <see cref="CameraDemoScreen.BallHitZone"/> — the system fires a burst
/// of trauma on the rising edge (in any camera mode) and flashes that square yellow
/// (<see cref="CameraDemoScreen.BlinkHitSquare"/>). The two squares give two distinct jolts:
///
/// - <b>Right square → positional shake.</b> Trauma drives a small, high-frequency random
///   jitter of <c>Camera.Position</c>.
/// - <b>Left square → rotational wobble.</b> Trauma drives a small, decaying cosine
///   oscillation of <c>Camera.Rotation</c>.
///
/// Each is a separate trauma that decays to zero over a fraction of a second; magnitude
/// falls off as trauma² for a sharp initial jolt that settles quickly. It is one hit per
/// entry (re-enter to re-trigger), and the dot's centered spawn lies in neither square, so
/// nothing fires on load.
///
/// It runs last in the update pipeline and writes <c>Camera.Position</c> / <c>Camera.Rotation</c>
/// after <see cref="CameraFollowSystem"/> — the documented composable pattern (the camera
/// overview's extension points: "write to the same Camera … last-write-wins per frame").
/// To keep the jolt from bleeding into the smoothed follow path (the follow system lerps
/// from <c>Camera.Position</c>), each frame it subtracts the offset and rotation it layered
/// on last frame to recover the clean base, then re-applies fresh ones on top.
public class CameraHitSystem : ISystem<GameState>
{
    // Peak positional offset in world units — kept small so the shake reads as a jolt.
    private const float MaxShakeOffset = 6f;
    // Peak rotation in radians (~2.9°) — a small tilt, not a roll.
    private const float MaxRotation = 0.05f;
    // Wobble frequency for the rotational hit — rapid (~7 Hz).
    private static readonly float RotateAngularFreq = MathHelper.TwoPi * 7f;
    // Trauma drains linearly to zero in 1 / TraumaDecayPerSecond seconds (~0.4s here),
    // so each hit is a brief burst rather than a sustained rumble.
    private const float TraumaDecayPerSecond = 2.5f;

    private readonly MonoDreams.Component.Camera _camera;
    private readonly CameraDemoScreen _screen;
    private readonly Random _rng = new();

    private Vector2 _appliedOffset = Vector2.Zero;
    private float _appliedRotation;
    private float _shakeTrauma;     // right square
    private float _rotateTrauma;    // left square
    private float _rotatePhase;     // seconds since the last left hit, for the cosine
    private CameraDemoScreen.HitZone _lastZone = CameraDemoScreen.HitZone.None;

    public bool IsEnabled { get; set; } = true;

    public CameraHitSystem(MonoDreams.Component.Camera camera, CameraDemoScreen screen)
    {
        _camera = camera;
        _screen = screen;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Rising edge: the dot just entered a hit square → take a hit and flash that
        // square. Right shakes, left rotates.
        var zone = _screen.BallHitZone;
        if (zone != CameraDemoScreen.HitZone.None && zone != _lastZone)
        {
            if (zone == CameraDemoScreen.HitZone.Right) _shakeTrauma = 1f;
            else { _rotateTrauma = 1f; _rotatePhase = 0f; }
            _screen.BlinkHitSquare(zone);
        }
        _lastZone = zone;

        _shakeTrauma = MathHelper.Clamp(_shakeTrauma - TraumaDecayPerSecond * state.Time, 0f, 1f);
        _rotateTrauma = MathHelper.Clamp(_rotateTrauma - TraumaDecayPerSecond * state.Time, 0f, 1f);
        _rotatePhase += state.Time;

        // Right-square shake: fresh random direction each frame → a rapid buzz, scaled by trauma².
        var shakeMag = MaxShakeOffset * _shakeTrauma * _shakeTrauma;
        var offset = shakeMag <= 0f
            ? Vector2.Zero
            : new Vector2(
                (float)(_rng.NextDouble() * 2.0 - 1.0),
                (float)(_rng.NextDouble() * 2.0 - 1.0)) * shakeMag;

        // Left-square rotate: a decaying cosine wobble (starts at full tilt on the hit), scaled by trauma².
        var angle = _rotateTrauma <= 0f
            ? 0f
            : MaxRotation * _rotateTrauma * _rotateTrauma * MathF.Cos(RotateAngularFreq * _rotatePhase);

        // Peel off last frame's own contribution before re-applying, so neither the shake
        // nor the rotation accumulates into the transform CameraFollowSystem smooths from.
        _camera.Position = (_camera.Position - _appliedOffset) + offset;
        _appliedOffset = offset;
        _camera.Rotation = (_camera.Rotation - _appliedRotation) + angle;
        _appliedRotation = angle;

        _screen.UpdateHitSquareBlinks(state.Time);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
