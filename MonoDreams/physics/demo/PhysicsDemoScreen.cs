using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Draw;
using MonoDreams.Message;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.System.Physics;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demo.Physics;

/// Physics block demo. 10 balls bounce inside a closed boundary under gravity. The
/// floor adds extra upward speed on contact so balls keep oscillating (the demo
/// is self-sustaining, not damped). 7 red balls collide with walls AND each other;
/// 3 blue balls only collide with walls (layer-isolated from reds; blue↔blue is
/// filtered in the bounce system).
public class PhysicsDemoScreen : IGameScreen
{
    private const float BoundaryHalfWidth  = 380f;
    private const float BoundaryHalfHeight = 220f;
    private const float WallThickness      = 20f;

    private const float Gravity      = 1200f;    // px/s²
    private const float Restitution  = 0.85f;    // wall/ball bounce energy retention
    private const float FloorBoost   = 260f;     // extra upward px/s on floor contact
    private const float MaxFallSpeed = 1400f;    // safety cap to prevent tunneling

    private const int RedBallLayer  = 0;
    private const int BlueBallLayer = 1;
    // Walls own both layers so red and blue both collide with them.

    /// 10 balls: 7 red (Type=Red) collide with walls + reds; 3 blue (Type=Blue)
    /// collide with walls only. Sizes mostly small with a couple of larger reds
    /// for visual variety.
    private static readonly BallSpec[] BallSpecs =
    {
        new(BallType.Red,   radius: 10f, spawn: new Vector2(-300, -160), velocity: new Vector2( 140,   0)),
        new(BallType.Red,   radius: 14f, spawn: new Vector2(-150, -160), velocity: new Vector2(-180,   0)),
        new(BallType.Red,   radius: 10f, spawn: new Vector2(   0, -160), velocity: new Vector2( 120,   0)),
        new(BallType.Red,   radius: 18f, spawn: new Vector2( 150, -160), velocity: new Vector2(-100,   0)),
        new(BallType.Red,   radius: 11f, spawn: new Vector2( 300, -160), velocity: new Vector2(-160,   0)),
        new(BallType.Red,   radius: 12f, spawn: new Vector2(-220,  -80), velocity: new Vector2( 200,   0)),
        new(BallType.Red,   radius: 22f, spawn: new Vector2( 220,  -80), velocity: new Vector2(-140,   0)),
        new(BallType.Blue,  radius: 13f, spawn: new Vector2( -80,  -80), velocity: new Vector2( -90,   0)),
        new(BallType.Blue,  radius: 20f, spawn: new Vector2(  80,  -80), velocity: new Vector2( 110,   0)),
        new(BallType.Blue,  radius: 15f, spawn: new Vector2(   0,    0), velocity: new Vector2( 160,   0)),
    };

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly IParallelRunner _runner;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;

    private ScreenController? _screenController;
    private readonly List<Entity> _balls = new();
    private Entity _gravityToggle;
    private Entity _floorBoostToggle;
    private bool _gravityOn = true;
    private bool _floorBoostOn = true;
    private bool _paused;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public PhysicsDemoScreen(GraphicsDevice graphicsDevice, ContentManager content,
        MonoDreams.Component.Camera camera, ViewportManager viewportManager, SpriteBatch spriteBatch,
        IParallelRunner runner)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _runner = runner;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI,   new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD,  new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
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
            [CursorType.Hand]    = content.Load<Texture2D>("Cursor/hand"),
        };
        MonoDreams.Cursor.Cursor.Create(_world, cursorTextures, RenderTargetID.HUD);

        CreateBoundary();
        CreateWalls();
        SpawnBalls();
        BuildHud(content);
    }

    // ─── public bridges for the keyboard system ───────────────────────────────

    public void GoBackToLauncher() => _screenController?.LoadScreen(DemoScreens.Launcher);

    public void TogglePause() => _paused = !_paused;

    public void ToggleGravity()
    {
        if (!_gravityToggle.IsAlive || !_gravityToggle.Has<ToggleSwitchComponent>()) return;
        var sw = _gravityToggle.Get<ToggleSwitchComponent>();
        sw.On = !sw.On;
        _gravityOn = sw.On;
        _gravityToggle.Set(sw);
    }

    public void ToggleFloorBoost()
    {
        if (!_floorBoostToggle.IsAlive || !_floorBoostToggle.Has<ToggleSwitchComponent>()) return;
        var sw = _floorBoostToggle.Get<ToggleSwitchComponent>();
        sw.On = !sw.On;
        _floorBoostOn = sw.On;
        _floorBoostToggle.Set(sw);
    }

    public void Reset()
    {
        for (var i = 0; i < _balls.Count && i < BallSpecs.Length; i++)
        {
            var ball = _balls[i];
            if (!ball.IsAlive) continue;

            ref var transform = ref ball.Get<TransformComponent>();
            transform.Position = BallSpecs[i].Spawn;
            transform.LastPosition = BallSpecs[i].Spawn;

            ref var velocity = ref ball.Get<VelocityComponent>();
            velocity.Current = BallSpecs[i].Velocity;
            velocity.Last = BallSpecs[i].Velocity;
        }
    }

    public bool GravityEnabled => _gravityOn && !_paused;
    public bool VelocityEnabled => !_paused;
    public bool FloorBoostEnabled => _floorBoostOn;

    // ─── button click routing ────────────────────────────────────────────────

    private void OnButtonClicked(in DemoButtonClicked msg)
    {
        switch (msg.Id)
        {
            case DemoHeader.BackId: _screenController?.LoadScreen(DemoScreens.Launcher); break;
            case DemoHeader.ExitId: _screenController?.Game.Exit(); break;
            case "physics.reset":      Reset(); break;
            case "physics.pause":      TogglePause(); break;
            case "toggle.gravity":     ToggleGravity(); break;
            case "toggle.floor-boost": ToggleFloorBoost(); break;
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

    /// Four BoxColliders forming a closed box just outside the visible boundary.
    /// Walls share both red and blue layers so both ball categories collide with
    /// them. Walls have zero TransformComponent.Delta so even though they're
    /// active colliders, the swept test self-skips when they iterate as entity A.
    private void CreateWalls()
    {
        var redAndBlue = new HashSet<int> { RedBallLayer, BlueBallLayer };

        // Floor — tagged so the bounce system can apply the extra upward kick.
        var floor = CreateWall(new Rectangle(
            -(int)(BoundaryHalfWidth + WallThickness),
            (int)BoundaryHalfHeight,
            (int)((BoundaryHalfWidth + WallThickness) * 2),
            (int)WallThickness), redAndBlue);
        floor.Set<FloorTag>();

        // Ceiling
        CreateWall(new Rectangle(
            -(int)(BoundaryHalfWidth + WallThickness),
            -(int)(BoundaryHalfHeight + WallThickness),
            (int)((BoundaryHalfWidth + WallThickness) * 2),
            (int)WallThickness), redAndBlue);

        // Left wall
        CreateWall(new Rectangle(
            -(int)(BoundaryHalfWidth + WallThickness),
            -(int)BoundaryHalfHeight,
            (int)WallThickness,
            (int)BoundaryHalfHeight * 2), redAndBlue);

        // Right wall
        CreateWall(new Rectangle(
            (int)BoundaryHalfWidth,
            -(int)BoundaryHalfHeight,
            (int)WallThickness,
            (int)BoundaryHalfHeight * 2), redAndBlue);
    }

    private Entity CreateWall(Rectangle bounds, HashSet<int> activeLayers)
    {
        var wall = _world.CreateEntity();
        wall.Set(new TransformComponent(Vector2.Zero));
        wall.Set(new BoxColliderComponent(bounds, activeLayers));
        return wall;
    }

    private void SpawnBalls()
    {
        for (var i = 0; i < BallSpecs.Length; i++)
        {
            var spec = BallSpecs[i];
            _balls.Add(CreateBall(spec));
        }
    }

    private Entity CreateBall(BallSpec spec)
    {
        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(spec.Spawn));
        entity.Set(new VelocityComponent(spec.Velocity));
        entity.Set(new RigidBodyComponent(mass: 1f));
        entity.Set(new BallTagComponent { Type = spec.Type });

        // Octagonal convex collider as a circle approximation. SAT handles
        // polygon-polygon collisions natively, so ball↔ball bounces look round
        // rather than square.
        var layer = spec.Type == BallType.Red ? RedBallLayer : BlueBallLayer;
        entity.Set(new ConvexColliderComponent(
            CircleVertices(spec.Radius, segments: 8),
            activeLayers: new HashSet<int> { layer }));

        var color = spec.Type == BallType.Red ? SproutPalette.Crimson : SproutPalette.SkyBlue;
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.95f };
        draw.SetMeshData(new CircleMeshGenerator(Vector2.Zero, spec.Radius, color, segments: 24));
        entity.Set(draw);
        entity.Set<VisibleComponent>();
        return entity;
    }

    private static Vector2[] CircleVertices(float radius, int segments)
    {
        var verts = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.PI * 2f * i / segments;
            verts[i] = new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius);
        }
        return verts;
    }

    // ─── HUD ────────────────────────────────────────────────────────────────

    private void BuildHud(ContentManager content)
    {
        var squareButtons = content.Load<Texture2D>("SproutLands/Buttons/square_26x26");
        var settingsSheet = content.Load<Texture2D>("SproutLands/Buttons/settings_buttons");

        DemoHeader.Build(
            _world, _viewportManager, _font, squareButtons,
            title: "physics",
            descriptionLines: new[]
            {
                "Ten balls bounce inside the boundary under gravity.",
                "Floor adds vertical speed on impact, so the system never settles.",
                "Red balls (7) collide with walls AND each other.",
                "Blue balls (3) collide with walls only — never with other balls.",
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

        var reset = Row("physics.reset", "R", "reset balls");
        var pause = Row("physics.pause", "_", "pause / resume");

        var gravity = _world.CreateToggleRow(
            id: "toggle.gravity",
            rowLabel: "gravity",
            font: _font,
            toggleSheet: settingsSheet,
            offSource: SproutSettings.ToggleOff,
            onSource:  SproutSettings.ToggleOn,
            initiallyOn: _gravityOn,
            toggleSize: new Vector2(42, 27),
            row: rowStyle,
            layerDepth: 0.96f);

        var floorBoost = _world.CreateToggleRow(
            id: "toggle.floor-boost",
            rowLabel: "floor boost",
            font: _font,
            toggleSheet: settingsSheet,
            offSource: SproutSettings.ToggleOff,
            onSource:  SproutSettings.ToggleOn,
            initiallyOn: _floorBoostOn,
            toggleSize: new Vector2(42, 27),
            row: rowStyle,
            layerDepth: 0.96f);

        _gravityToggle = gravity.Outline;
        _floorBoostToggle = floorBoost.Outline;

        const float rowGap = 6f;
        const float groupGap = 16f;

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopLeft, RenderTargetID.HUD)
            .Direction(LayoutDirection.Vertical)
            .Gap(rowGap)
            .Padding(20, 12, 12, 12)
            .AlignCross(CrossAxisAlignment.Start)
            .AddSlot(slot => slot.Attach(reset.Container).MeasureWith(_ => reset.Size))
            .AddSlot(slot => slot.Attach(pause.Container).MeasureWith(_ => pause.Size))
            .AddSlot(slot => slot.Attach(_world.CreateEntity()).MeasureWith(_ => new Vector2(0, groupGap - rowGap)))
            .AddSlot(slot => slot.Attach(gravity.Container).MeasureWith(_ => gravity.Size))
            .AddSlot(slot => slot.Attach(floorBoost.Container).MeasureWith(_ => floorBoost.Size))
            .Build();
    }

    // ─── pipeline ────────────────────────────────────────────────────────────

    /// Adapter to the `CreateCollisionMessageDelegate<CollisionMessage>` shape that
    /// `TransformCollisionDetectionSystem` expects. Always emits Physics-type messages.
    private static CollisionMessage CreateCollisionMessage(
        Entity entity, Entity target,
        Vector2 contactPoint, Vector2 contactNormal,
        float contactTime, float penetrationDepth, int layer)
        => new(entity, target, contactPoint, contactNormal, contactTime, penetrationDepth, layer, CollisionType.Physics);

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        // Reference physics pipeline order (per docs/CORE_TENETS.md §5):
        //   Movement → Velocity → Detection → Resolution → Commit
        // Movement here is gravity-only; ball↔ball/ball↔wall bouncing is the
        // custom resolution stage that subscribes to CollisionMessage.
        return new SequentialSystem<GameState>(
            new CursorInputSystem(_world),
            new IntrinsicSizingSystem(_world),
            new AutoLayoutSystem(_world, _viewportManager),
            new DemoButtonInteractionSystem(_world),
            new DemoIconRecolorSystem(_world),
            new ToggleSwitchSystem(_world),
            new PhysicsDemoInputSystem(this),
            new GatedGravitySystem(_world, _runner, this),
            new GatedVelocitySystem(_world, _runner, this),
            new TransformCollisionDetectionSystem<CollisionMessage>(_world, CreateCollisionMessage),
            new BallBounceSystem(_world, this),
            new TransformCommitSystem(_world, _runner),
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

// ─── tags + data ──────────────────────────────────────────────────────────

public enum BallType { Red, Blue }

public struct BallTagComponent { public BallType Type; }

/// Marks the bottom wall so the bounce system can add the upward kick that
/// keeps the demo perpetual.
public struct FloorTag { }

internal readonly struct BallSpec
{
    public readonly BallType Type;
    public readonly float Radius;
    public readonly Vector2 Spawn;
    public readonly Vector2 Velocity;
    public BallSpec(BallType type, float radius, Vector2 spawn, Vector2 velocity)
    {
        Type = type; Radius = radius; Spawn = spawn; Velocity = velocity;
    }
}

// ─── input ────────────────────────────────────────────────────────────────

/// Edge-triggered keyboard shortcuts for the physics demo.
public class PhysicsDemoInputSystem : ISystem<GameState>
{
    private readonly PhysicsDemoScreen _screen;
    private KeyboardState _previous;
    public bool IsEnabled { get; set; } = true;

    public PhysicsDemoInputSystem(PhysicsDemoScreen screen)
    {
        _screen = screen;
        _previous = Keyboard.GetState();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        var current = Keyboard.GetState();
        bool Pressed(Keys k) => current.IsKeyDown(k) && !_previous.IsKeyDown(k);

        if (Pressed(Keys.R)) _screen.Reset();
        if (Pressed(Keys.Space)) _screen.TogglePause();
        if (Pressed(Keys.G)) _screen.ToggleGravity();
        if (Pressed(Keys.F)) _screen.ToggleFloorBoost();
        if (Pressed(Keys.Escape)) _screen.GoBackToLauncher();

        _previous = current;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

// ─── physics systems (gated by demo state) ────────────────────────────────

/// Wraps the engine's GravitySystem so it can be paused or toggled off by the
/// demo without re-registering the system on toggle.
public class GatedGravitySystem : ISystem<GameState>
{
    private readonly GravitySystem _inner;
    private readonly PhysicsDemoScreen _screen;
    public bool IsEnabled { get; set; } = true;

    public GatedGravitySystem(World world, IParallelRunner runner, PhysicsDemoScreen screen)
    {
        _screen = screen;
        // 1400 max-fall cap matches PhysicsDemoScreen.MaxFallSpeed to prevent
        // tunneling at high velocity.
        _inner = new GravitySystem(world, runner, worldGravity: 1200f, maxFallVelocity: 1400f);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled || !_screen.GravityEnabled) return;
        _inner.Update(state);
    }

    public void Dispose()
    {
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// Wraps TransformVelocitySystem so pause halts position updates without
/// dismantling the pipeline.
public class GatedVelocitySystem : ISystem<GameState>
{
    private readonly TransformVelocitySystem _inner;
    private readonly PhysicsDemoScreen _screen;
    public bool IsEnabled { get; set; } = true;

    public GatedVelocitySystem(World world, IParallelRunner runner, PhysicsDemoScreen screen)
    {
        _screen = screen;
        _inner = new TransformVelocitySystem(world, runner);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled || !_screen.VelocityEnabled) return;
        _inner.Update(state);
    }

    public void Dispose()
    {
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ─── bounce resolution ────────────────────────────────────────────────────

/// Custom resolution: subscribes to <see cref="CollisionMessage"/>, pushes the
/// base entity out by the MTV, then reflects its velocity along the contact
/// normal (with restitution). A floor-tagged collider adds an extra upward
/// impulse so the system stays in motion.
///
/// Replaces the engine's <see cref="TransformCollisionResolutionSystem{T}"/>
/// for this demo — that system zeros velocity along the normal (kinematic
/// "stop"), which would defeat the bouncing behaviour we want here.
public class BallBounceSystem : ISystem<GameState>
{
    private const float Restitution = 0.85f;
    private const float FloorBoost = 260f;

    private readonly World _world;
    private readonly PhysicsDemoScreen _screen;
    private readonly List<CollisionMessage> _collisions = new();

    public bool IsEnabled { get; set; } = true;

    public BallBounceSystem(World world, PhysicsDemoScreen screen)
    {
        _world = world;
        _screen = screen;
        world.Subscribe<CollisionMessage>(OnCollision);
    }

    private void OnCollision(in CollisionMessage msg) => _collisions.Add(msg);

    public void Update(GameState state)
    {
        if (!IsEnabled)
        {
            _collisions.Clear();
            return;
        }

        foreach (var msg in _collisions)
        {
            Resolve(msg);
        }
        _collisions.Clear();
    }

    private void Resolve(CollisionMessage msg)
    {
        var entity = msg.BaseEntity;
        var other  = msg.CollidingEntity;
        if (!entity.IsAlive || !other.IsAlive) return;

        // Walls have no velocity component (and shouldn't move); guard.
        if (!entity.Has<VelocityComponent>() || !entity.Has<TransformComponent>()) return;

        // Filter blue↔blue: blue balls only collide with walls. They share a
        // layer (BlueBallLayer), so the detection system emits the message; we
        // discard it here. (Red↔blue is already filtered by layer mismatch.)
        if (entity.Has<BallTagComponent>() && other.Has<BallTagComponent>())
        {
            var a = entity.Get<BallTagComponent>().Type;
            var b = other.Get<BallTagComponent>().Type;
            if (a == BallType.Blue && b == BallType.Blue) return;
        }

        ref var transform = ref entity.Get<TransformComponent>();
        ref var velocity = ref entity.Get<VelocityComponent>();

        var normal = msg.ContactNormal;
        if (normal == Vector2.Zero) return;
        var penetration = msg.PenetrationDepth;

        // SAT normal points from A's center toward B's center, i.e. "into the
        // collision" from A's perspective. Push A out along -normal.
        // For ball↔ball we apply half-push because the symmetric message
        // (A=B, B=A) will arrive too and push the other half; for ball↔wall
        // the wall is static and the swept self-skip ensures it never iterates
        // as A, so a single full push is correct here.
        var isBallVsBall = entity.Has<BallTagComponent>() && other.Has<BallTagComponent>();
        var pushScale = isBallVsBall ? 0.5f : 1f;
        transform.Translate(-normal * penetration * pushScale);

        // Reflect velocity along the contact normal with restitution. Only do
        // it if velocity is moving INTO the collision (dot > 0); otherwise the
        // ball is already separating and we'd accidentally accelerate it.
        var vDotN = Vector2.Dot(velocity.Current, normal);
        if (vDotN > 0)
        {
            velocity.Current -= (1f + Restitution) * vDotN * normal;
        }

        // Floor kick: extra upward impulse on top of the elastic bounce.
        if (_screen.FloorBoostEnabled && other.Has<FloorTag>())
        {
            // Subtract because Y-down: upward is -Y.
            velocity.Current.Y -= FloorBoost;
        }
    }

    public void Dispose()
    {
        _collisions.Clear();
        GC.SuppressFinalize(this);
    }
}
