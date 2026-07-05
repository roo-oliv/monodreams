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
using MonoDreams.Demos;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Composition;
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

/// Physics module demo. 10 balls launch in random directions and bounce inside a
/// closed boundary under gravity. The floor adds extra upward speed on contact so
/// balls keep oscillating (the demo is self-sustaining, not damped). Solid red balls
/// collide with walls AND each other; hollow (outline-only) blue balls only collide
/// with walls (layer-isolated from reds; blue↔blue is filtered in the bounce system).
///
/// Visual feedback (FlashComponent + FlashSystem): a wall hit blinks the ball's own
/// vivid tint; two balls colliding pop a brighter flash on BOTH balls regardless of
/// speed. The floor is a thick line — cream when the boost is off, green when on —
/// and blinks bright green when a ball lands on it while the boost is on.
public class PhysicsDemoScreen : IGameScreen
{
    private const float BoundaryHalfWidth  = 380f;
    private const float BoundaryHalfHeight = 220f;
    private const float WallThickness      = 20f;

    private const float Gravity      = 1200f;    // px/s²
    private const float Restitution  = 0.92f;    // wall/ball bounce energy retention
    private const float FloorBoost   = 260f;     // extra upward px/s on floor contact
    private const float MaxFallSpeed = 1400f;    // safety cap to prevent tunneling

    private const float FloorVisualThickness = 6f;     // thick floor line vs the 2px boundary
    private const float FlashDuration        = 0.25f;  // seconds for a blink to fade back

    private const float MinSpawnSpeed = 120f;    // random initial-velocity speed range
    private const float MaxSpawnSpeed = 220f;
    private const float MinBallRadius = 5f;      // random per-ball size range
    private const float MaxBallRadius = 10f;
    private const float BlueBallBorder = 2f;     // outline thickness for the hollow blue balls
    // Collider and rendered circle share this polygon resolution so the contact
    // shape is exactly the drawn silhouette (see CreateBall). Tangency no longer
    // depends on it — only roundness-of-look and narrowphase cost do — so it can be
    // lowered for cheaper narrowphase at very high ball counts.
    private const int BallSegments = 32;
    // Speed clamp keeps the self-sustaining floor-boost loop from compounding into
    // tunnelling speeds. Sits just above a full-height free-fall (~1030 px/s) so
    // normal bounces are untouched but runaway energy is capped.
    private const float MaxBallSpeed  = 1050f;

    private const int DefaultRedCount  = 7;
    private const int DefaultBlueCount = 3;
    private const int MaxBallsPerColor = 999999; // clamp on the editable counts

    /// Floor line tints: regular (boost off), green (boost on), bright blink on a boosted hit.
    private static readonly Color FloorRegularColor = DemoPalette.TextLight;
    private static readonly Color FloorActiveColor  = new(106, 190, 89);
    private static readonly Color FloorFlashColor   = new(190, 255, 150);

    private const int RedBallLayer  = 0;
    private const int BlueBallLayer = 1;
    // Walls own both layers so red and blue both collide with them.

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
    private readonly Random _rng = new();
    private Entity _gravityToggle;
    private Entity _floorBoostToggle;
    private Entity _floorVisual;
    private Entity _redInput;
    private Entity _blueInput;
    private int _redCount = DefaultRedCount;
    private int _blueCount = DefaultBlueCount;
    private bool _gravityOn = true;
    private bool _floorBoostOn = true;
    private bool _paused;

    // The universal editor overlay (null when editorEnabled is false) and the retained pipeline
    // registries the editor's systems panel binds to (see DemoEditor).
    private readonly bool _editorEnabled;
    private readonly DrawLayerMap _layers = DemoEditor.CreateLayers();
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private DemoEditor? _editor;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public PhysicsDemoScreen(GraphicsDevice graphicsDevice, ContentManager content,
        MonoDreams.Component.Camera camera, ViewportManager viewportManager, SpriteBatch spriteBatch,
        IParallelRunner runner, bool editorEnabled = false)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _runner = runner;
        _editorEnabled = editorEnabled;
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

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.Overlay.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(PhysicsDemoScreen), _updatePipeline, _drawPipeline);
        }
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnButtonClicked);
        _world.Subscribe<TextInputChanged>(OnTextInputChanged);

        MonoDreams.Cursor.Cursor.CreateMesh(_world,
            ShapeBuilder.Arrow(26f, Color.Black, Color.White).Generate(), RenderTargetID.HUD);

        CreateBoundary();
        CreateFloorVisual();
        CreateWalls();
        RebuildBalls();
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
        WakeAllBalls();
    }

    public void ToggleFloorBoost()
    {
        if (!_floorBoostToggle.IsAlive || !_floorBoostToggle.Has<ToggleSwitchComponent>()) return;
        var sw = _floorBoostToggle.Get<ToggleSwitchComponent>();
        sw.On = !sw.On;
        _floorBoostOn = sw.On;
        _floorBoostToggle.Set(sw);
        UpdateFloorBaseColor();
        WakeAllBalls();
    }

    /// Floor line is green while the boost is on, regular cream while it's off.
    /// The FlashSystem repaints it to this base color whenever it's not blinking.
    private void UpdateFloorBaseColor()
    {
        if (!_floorVisual.IsAlive || !_floorVisual.Has<FlashComponent>()) return;
        ref var flash = ref _floorVisual.Get<FlashComponent>();
        flash.BaseColor = _floorBoostOn ? FloorActiveColor : FloorRegularColor;
    }

    /// Reset spawns a fresh random layout honoring the current red/blue counts.
    public void Reset() => RebuildBalls();

    /// Wakes every ball (clears the asleep/still state from <see cref="BallRestSystem"/>)
    /// so a fresh disturbance — flipping gravity or floor boost — re-energises a settled
    /// pile instead of leaving it frozen in place.
    public void WakeAllBalls()
    {
        foreach (var ball in _balls)
        {
            if (!ball.IsAlive || !ball.Has<BallTagComponent>()) continue;
            ref var tag = ref ball.Get<BallTagComponent>();
            tag.Asleep = false;
            tag.StillTime = 0f;
        }
    }

    public bool GravityEnabled => _gravityOn && !_paused;
    public bool VelocityEnabled => !_paused;
    public bool FloorBoostEnabled => _floorBoostOn;

    /// The thick floor line entity, blinked by the bounce system on a boosted hit.
    public Entity FloorVisual => _floorVisual;

    /// A random launch velocity: random direction, random speed in the spawn range.
    private Vector2 RandomVelocity()
    {
        var angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
        var speed = MinSpawnSpeed + (float)_rng.NextDouble() * (MaxSpawnSpeed - MinSpawnSpeed);
        return new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed);
    }

    // ─── button click routing ────────────────────────────────────────────────

    private const string RedInputId  = "input.red";
    private const string BlueInputId = "input.blue";

    private void OnButtonClicked(in DemoButtonClicked msg)
    {
        // Clicking a number box focuses it (exclusively); clicking anything else
        // blurs both so keyboard shortcuts resume.
        switch (msg.Id)
        {
            case RedInputId:  FocusInput(_redInput);  return;
            case BlueInputId: FocusInput(_blueInput); return;
        }

        BlurInputs();
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

    private void OnTextInputChanged(in TextInputChanged msg)
    {
        if (msg.Input == _redInput)
        {
            var n = ParseCount(msg.Text);
            if (n == _redCount) return;
            _redCount = n;
        }
        else if (msg.Input == _blueInput)
        {
            var n = ParseCount(msg.Text);
            if (n == _blueCount) return;
            _blueCount = n;
        }
        else return;

        RebuildBalls();
    }

    private static int ParseCount(string text) =>
        int.TryParse(text, out var v) ? Math.Clamp(v, 0, MaxBallsPerColor) : 0;

    // ─── focus (game-owned; TextInputSystem only reads the flag) ───────────────

    private void FocusInput(Entity target)
    {
        SetFocus(_redInput,  target == _redInput);
        SetFocus(_blueInput, target == _blueInput);
    }

    private void BlurInputs()
    {
        SetFocus(_redInput,  false);
        SetFocus(_blueInput, false);
    }

    /// Mirrors focus onto both the input's key-capture flag and its button accent
    /// (IsActive drives the focused border/text color via DemoButtonInteractionSystem).
    private static void SetFocus(Entity input, bool focused)
    {
        if (!input.IsAlive) return;
        if (input.Has<TextInputComponent>())
        {
            ref var ti = ref input.Get<TextInputComponent>();
            ti.Focused = focused;
        }
        if (input.Has<DemoButtonComponent>())
        {
            ref var db = ref input.Get<DemoButtonComponent>();
            db.IsActive = focused;
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

    /// Thick horizontal line drawn over the boundary's bottom edge — the floor's
    /// visual. Regular cream when the boost is off, green when on, and it blinks
    /// bright (via FlashSystem) when a ball lands on it while the boost is on.
    /// Sits at LayerDepth 0.25 — above the boundary outline (0.2), below the balls.
    private void CreateFloorVisual()
    {
        var left  = new Vector2(-BoundaryHalfWidth, BoundaryHalfHeight);
        var right = new Vector2( BoundaryHalfWidth, BoundaryHalfHeight);
        var baseColor = _floorBoostOn ? FloorActiveColor : FloorRegularColor;

        var floor = _world.CreateEntity();
        floor.Set(new TransformComponent(Vector2.Zero));
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.25f };
        draw.SetMeshData(new LineMeshGenerator(left, right, FloorVisualThickness, baseColor));
        floor.Set(draw);
        floor.Set(new FlashComponent(baseColor, FloorFlashColor, FlashDuration));
        floor.Set<VisibleComponent>();
        _floorVisual = floor;
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

    /// Despawns the current balls and spawns a fresh random set matching the
    /// current red/blue counts. Called on load, on reset, and whenever a count
    /// input changes.
    private void RebuildBalls()
    {
        foreach (var ball in _balls)
            if (ball.IsAlive) ball.Dispose();
        _balls.Clear();

        for (var i = 0; i < _redCount; i++)  _balls.Add(CreateBall(BallType.Red));
        for (var i = 0; i < _blueCount; i++) _balls.Add(CreateBall(BallType.Blue));
    }

    private Entity CreateBall(BallType type)
    {
        var radius = MinBallRadius + (float)_rng.NextDouble() * (MaxBallRadius - MinBallRadius);
        var spawn = RandomSpawn(radius);

        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(spawn));
        entity.Set(new VelocityComponent(RandomVelocity()));
        entity.Set(new RigidBodyComponent(mass: 1f));
        entity.Set(new BallTagComponent { Type = type });

        // Convex collider with BallSegments sides, built from the same radius and
        // angles as the rendered circle below, so the collider polygon coincides
        // with the drawn silhouette. That makes resting balls sit visually tangent:
        // an inscribed octagon would sit inside the rounder render, letting the
        // circles overlap even at perfect collider contact. SAT handles the
        // polygon-polygon bounce natively.
        var layer = type == BallType.Red ? RedBallLayer : BlueBallLayer;
        entity.Set(new ConvexColliderComponent(
            CircleVertices(radius, segments: BallSegments),
            activeLayers: new HashSet<int> { layer }));

        // Resting tint + the vivid color the ball blinks to on a wall hit (FlashSystem).
        var (color, flashColor) = BallColors(type);
        var draw = new DrawComponent { Target = RenderTargetID.Main, LayerDepth = 0.95f };
        // Red balls are solid; blue balls are hollow — just their painted border.
        // BallSegments matches the collider so the contact shape is the drawn shape.
        IMeshGenerator mesh = type == BallType.Blue
            ? new CircleOutlineMeshGenerator(Vector2.Zero, radius, BlueBallBorder, color, segments: BallSegments)
            : new CircleMeshGenerator(Vector2.Zero, radius, color, segments: BallSegments);
        draw.SetMeshData(mesh);
        entity.Set(draw);
        entity.Set(new FlashComponent(color, flashColor, FlashDuration));
        entity.Set<VisibleComponent>();
        return entity;
    }

    /// A ball type's (resting tint, wall-hit flash tint). Single source of truth so
    /// the spawn code and the bounce system agree on a ball's colors.
    public static (Color Resting, Color Flash) BallColors(BallType type) => type == BallType.Red
        ? (DemoPalette.Crimson, new Color(255, 90, 80))
        : (DemoPalette.SkyBlue, new Color(150, 230, 255));

    /// A random spawn point inside the boundary, kept a ball-radius clear of the walls.
    private Vector2 RandomSpawn(float radius)
    {
        var margin = radius + 8f;
        var x = MathHelper.Lerp(-BoundaryHalfWidth + margin,  BoundaryHalfWidth - margin,  (float)_rng.NextDouble());
        var y = MathHelper.Lerp(-BoundaryHalfHeight + margin, BoundaryHalfHeight - margin, (float)_rng.NextDouble());
        return new Vector2(x, y);
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
        DemoHeader.Build(
            _world, _viewportManager, _font,
            title: "physics",
            descriptionLines: new[]
            {
                "Balls bounce inside the box under gravity.",
                "Red balls collide with each other; blue balls don't.",
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

        var reset = Row("physics.reset", "R", "reset balls");
        var pause = Row("physics.pause", "_", "pause / resume");

        var gravity = _world.CreateCheckboxRow(
            id: "toggle.gravity",
            rowLabel: "gravity",
            font: _font,
            initiallyOn: _gravityOn,
            boxSize: 42f,
            row: rowStyle,
            layerDepth: 0.96f);

        var floorBoost = _world.CreateCheckboxRow(
            id: "toggle.floor-boost",
            rowLabel: "floor boost",
            font: _font,
            initiallyOn: _floorBoostOn,
            boxSize: 42f,
            row: rowStyle,
            layerDepth: 0.96f);

        _gravityToggle = gravity.Outline;
        _floorBoostToggle = floorBoost.Outline;

        // Counts accept up to MaxBallsPerColor; size the box so its widest value
        // (all 9s) fits without overflowing the border.
        var maxDigits = MaxBallsPerColor.ToString();
        const float inputTextScale = 0.2f;
        const float inputBoxPadding = 8f;
        var inputBoxWidth = _font.MeasureString(new string('9', maxDigits.Length)).Width * inputTextScale
                            + inputBoxPadding * 2f;

        var inputStyle = new NumberInputStyle
        {
            LabelColor = DemoPalette.TextLight,
            AccentColor = DemoPalette.TextLight,
            HoverColor = DemoPalette.TextHover,
            FocusColor = DemoPalette.TextSelected,
            FillColor = DemoPalette.DarkBgSecondary,
            BorderThickness = 2f,
            LabelScale = 0.18f,
            TextScale = inputTextScale,
            Gap = 10f,
            BoxSize = new Vector2(inputBoxWidth, 30),
            BoxPadding = inputBoxPadding,
        };

        var redInput = _world.CreateNumberInputRow(
            RedInputId, "red balls", _redCount.ToString(), maxLength: maxDigits.Length, _font, inputStyle, layerDepth: 0.96f);
        var blueInput = _world.CreateNumberInputRow(
            BlueInputId, "blue balls", _blueCount.ToString(), maxLength: maxDigits.Length, _font, inputStyle, layerDepth: 0.96f);

        _redInput = redInput.Outline;
        _blueInput = blueInput.Outline;

        const float rowGap = 6f;
        const float groupGap = 16f;

        Entity Spacer() => _world.CreateEntity();

        new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.TopLeft, RenderTargetID.HUD)
            .Direction(LayoutDirection.Vertical)
            .Gap(rowGap)
            .Padding(20, 12, 12, 12)
            .AlignCross(CrossAxisAlignment.Start)
            .AddSlot(slot => slot.Attach(reset.Container).MeasureWith(_ => reset.Size))
            .AddSlot(slot => slot.Attach(pause.Container).MeasureWith(_ => pause.Size))
            .AddSlot(slot => slot.Attach(Spacer()).MeasureWith(_ => new Vector2(0, groupGap - rowGap)))
            .AddSlot(slot => slot.Attach(gravity.Container).MeasureWith(_ => gravity.Size))
            .AddSlot(slot => slot.Attach(floorBoost.Container).MeasureWith(_ => floorBoost.Size))
            .AddSlot(slot => slot.Attach(Spacer()).MeasureWith(_ => new Vector2(0, groupGap - rowGap)))
            .AddSlot(slot => slot.Attach(redInput.Container).MeasureWith(_ => redInput.Size))
            .AddSlot(slot => slot.Attach(blueInput.Container).MeasureWith(_ => blueInput.Size))
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
        var cursorInputSystem = new CursorInputSystem(_world, _viewportManager);

        // The editor overlay (see DemoEditor): built over THIS screen's world/camera/layers.
        _editor = DemoEditor.TryCreate(_editorEnabled, _world, _camera, _layers, _content,
            _graphicsDevice, _spriteBatch, _viewportManager, () => _screenController?.Game);
        // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
        if (_editor?.Overlay.HasEditorOpPlan == true) cursorInputSystem.SkipHardwareRead = true;

        // ---- Weave the update pipeline through the registrar. With the editor off every gate
        // is a pass-through in Play and the order matches the pre-editor screen exactly. ----
        var p = _updatePipeline;
        p.Add("input", cursorInputSystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.keys", _editor.Keys, EditTimeBehavior.RunNormally);
            p.Add("editor.sceneReader", _editor.Overlay.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Overlay.Dialog, EditTimeBehavior.RunNormally);
        }
        p.AddGroup("layout", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("intrinsicSizing", new IntrinsicSizingSystem(_world));
            g.Add("autoLayout", new AutoLayoutSystem(_world, _viewportManager));
        });
        // Demo UI interaction FREEZES in Edit: a click belongs to the editor, never to a
        // toggle / text field / back / exit (which would tear the screen down mid-editing).
        p.AddGroup("ui.interaction", EditTimeBehavior.Freeze, g =>
        {
            g.Add("buttons", new DemoButtonInteractionSystem(_world));
            g.Add("textInput", new TextInputSystem(_world));
            g.Add("toggles", new ToggleSwitchSystem(_world));
        });
        // Reference physics pipeline order (per docs/CORE_TENETS.md §5):
        //   Movement → Velocity → Detection → Resolution → Commit
        // Movement here is gravity-only; ball↔ball/ball↔wall bouncing is the
        // custom resolution stage that subscribes to CollisionMessage.
        // The WHOLE simulation freezes in Edit — it would bounce the balls out from under the
        // designer (and the gizmo) every frame. One Freeze gate on the group.
        p.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("demoInput", new PhysicsDemoInputSystem(this));
            g.Add("gravity", new GatedGravitySystem(_world, _runner, this));
            g.Add("rest", new BallRestSystem(_world, this));
            g.Add("velocity", new GatedVelocitySystem(_world, _runner, this));
            g.Add("collisionDetect",
                new TransformCollisionDetectionSystem<CollisionMessage>(_world, CreateCollisionMessage));
            g.Add("bounce", new BallBounceSystem(_world, this));
            g.Add("speedClamp", new BallSpeedClampSystem(_world, MaxBallSpeed));
            g.Add("flash", new FlashSystem(_world));
            g.Add("transformCommit", new TransformCommitSystem(_world, _runner));
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
}

// ─── tags + data ──────────────────────────────────────────────────────────

public enum BallType { Red, Blue }

public struct BallTagComponent
{
    public BallType Type;
    public float StillTime;   // seconds spent below the sleep speed (BallRestSystem)
    public bool Asleep;       // frozen: gravity cancelled + not integrated until woken
}

/// Marks the bottom wall so the bounce system can add the upward kick that
/// keeps the demo perpetual.
public struct FloorTag { }

/// Demo-local "blink on impact" tint for any mesh entity. <see cref="FlashSystem"/>
/// lerps the mesh's vertex colors from <see cref="FlashColor"/> back to
/// <see cref="BaseColor"/> as <see cref="Remaining"/> decays to zero; setting
/// <c>Remaining = Duration</c> (re)triggers the blink. Pure data — the system owns
/// the timing and the vertex writes.
public struct FlashComponent
{
    public Color BaseColor;
    public Color FlashColor;
    public float Duration;
    public float Remaining;

    public FlashComponent(Color baseColor, Color flashColor, float duration)
    {
        BaseColor = baseColor;
        FlashColor = flashColor;
        Duration = duration;
        Remaining = 0f;
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

/// Custom resolution: subscribes to <see cref="CollisionMessage"/>, softly pushes
/// the base entity out of penetration (slop + partial correction), then applies a
/// relative-velocity impulse along the contact normal — so a ball struck while at
/// rest takes on the incoming ball's momentum instead of acting like a wall. A fast
/// contact bounces elastically (with restitution); a slow one below <c>RestThreshold</c>
/// is treated as resting — the approach is merely cancelled — so dense piles settle
/// instead of jittering. A floor-tagged collider adds an extra upward impulse so the
/// boost showcase stays in motion.
///
/// Replaces the engine's <see cref="TransformCollisionResolutionSystem{T}"/>
/// for this demo — that system zeros velocity along the normal (kinematic
/// "stop"), which would defeat the bouncing behaviour we want here.
public class BallBounceSystem : ISystem<GameState>
{
    private const float Restitution = 0.92f;     // energy retention per bounce (higher = less loss)
    private const float FloorBoost = 260f;
    private const float FlashImpactSpeed = 40f;  // min closing speed that counts as a wall impact

    // Resting-contact handling so dense piles settle instead of jittering. Below
    // RestThreshold a contact is "resting": restitution drops to 0 (cancel only the
    // inward normal velocity) instead of bouncing, so gravity's ~20 px/s per-frame
    // nudge no longer micro-bounces a ball forever. PenetrationSlop + PositionCorrection
    // do Baumgarte-style positional correction — push out most of the overlap each
    // frame, leaving a sub-pixel slop — so balls sit visually tangent rather than sunk
    // into each other. The slop is also a deadband: once a settled (asleep) pile relaxes
    // below it, corrections stop entirely, so the pile reaches a true fixed point instead
    // of perpetually nudging. Damped below 1.0 so packed neighbours don't over-separate.
    // All of this engages only at low speed, so active bouncing (and the floor-boost
    // showcase) is untouched.
    private const float RestThreshold      = 50f;   // closing speed below which a contact is "resting"
    private const float PenetrationSlop    = 0.2f;  // overlap (px) left uncorrected — a deadband for stillness
    private const float PositionCorrection = 0.8f;  // fraction of the remaining overlap pushed out per frame

    /// Bright pop both balls blink to when two balls collide — distinct from (and
    /// brighter than) the per-ball wall-hit tint, and fired regardless of speed.
    private static readonly Color BallCollisionFlashColor = new(255, 255, 235);

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

        // Soft positional correction: leave a slop of overlap and push out only a
        // fraction of the rest this frame. Full immediate correction makes packed
        // neighbours fight (resolve A↔B → A penetrates C → resolve A↔C → back),
        // which reads as jitter; the small residual overlap is sub-pixel.
        var correction = MathF.Max(0f, penetration - PenetrationSlop) * PositionCorrection;
        if (correction > 0f)
            transform.Translate(-normal * correction * pushScale);

        // Velocity response uses the RELATIVE normal velocity (closing speed), so a
        // ball hit while at rest still absorbs the impact instead of acting like a
        // wall. Equal-mass balls split the impulse — each symmetric message (A→B and
        // B→A) applies half, summing to the full two-body exchange — while a ball↔wall
        // contact (infinite mass, no symmetric message) takes the full reflection.
        // With other's velocity zero this reduces exactly to reflecting the ball's own
        // velocity, so wall bounces and head-on ball pairs are unchanged. Only act when
        // closing (> 0); a separating pair is already parting and must not be glued.
        var otherVelocity = other.Has<VelocityComponent>()
            ? other.Get<VelocityComponent>().Current
            : Vector2.Zero;
        var closingSpeed = Vector2.Dot(velocity.Current - otherVelocity, normal);
        var isResting = closingSpeed < RestThreshold;
        if (closingSpeed > 0f)
        {
            // Resting contact → restitution 0: just kill the approach so the ball
            // settles (tangential motion survives, so it can still slide/roll). A real
            // impact → elastic exchange with restitution.
            var restitution = isResting ? 0f : Restitution;
            var massFactor = isBallVsBall ? 0.5f : 1f;
            velocity.Current -= massFactor * (1f + restitution) * closingSpeed * normal;

            // A genuine impact (above the rest threshold) wakes a settled ball so
            // gravity and integration resume and the impulse above isn't frozen away
            // next frame by BallRestSystem. Resting contacts stay below it, so a quiet
            // pile never wakes itself.
            if (!isResting)
            {
                Wake(entity);
                Wake(other);
            }
        }

        var hitFloor = other.Has<FloorTag>();

        // Floor kick: extra upward impulse on top of the elastic bounce.
        if (_screen.FloorBoostEnabled && hitFloor)
        {
            // Subtract because Y-down: upward is -Y.
            velocity.Current.Y -= FloorBoost;
        }

        // Two balls colliding pop a bright flash on BOTH balls — independent of which
        // is this message's base entity. Gated by the rest threshold (not by which
        // ball is faster) so a genuine hit at any real speed flashes, but balls in a
        // settled pile (sustained sub-threshold contact) decay back to their resting
        // tint instead of glowing at the flash color forever. Wall hits keep the
        // per-ball tint, gated by a minimum closing speed.
        if (isBallVsBall)
        {
            if (!isResting)
            {
                Flash(entity, BallCollisionFlashColor);
                Flash(other, BallCollisionFlashColor);
            }
        }
        else if (closingSpeed > FlashImpactSpeed)
        {
            Flash(entity, PhysicsDemoScreen.BallColors(entity.Get<BallTagComponent>().Type).Flash);
            if (hitFloor && _screen.FloorBoostEnabled) Flash(_screen.FloorVisual);
        }
    }

    /// Wakes a sleeping ball so gravity and integration resume next frame
    /// (<see cref="BallRestSystem"/> stops freezing it once <c>Asleep</c> clears).
    private static void Wake(Entity e)
    {
        if (!e.IsAlive || !e.Has<BallTagComponent>()) return;
        ref var tag = ref e.Get<BallTagComponent>();
        if (!tag.Asleep && tag.StillTime == 0f) return;
        tag.Asleep = false;
        tag.StillTime = 0f;
    }

    /// (Re)trigger an entity's blink, keeping its existing <see cref="FlashComponent.FlashColor"/>.
    private static void Flash(Entity e)
    {
        if (!e.IsAlive || !e.Has<FlashComponent>()) return;
        ref var flash = ref e.Get<FlashComponent>();
        flash.Remaining = flash.Duration;
    }

    /// (Re)trigger an entity's blink with an explicit color. <see cref="FlashSystem"/> shows
    /// <c>BaseColor</c> when idle, so overwriting <c>FlashColor</c> here only colors the blink
    /// this call starts — letting wall hits and ball↔ball hits blink different colors.
    private static void Flash(Entity e, Color flashColor)
    {
        if (!e.IsAlive || !e.Has<FlashComponent>()) return;
        ref var flash = ref e.Get<FlashComponent>();
        flash.FlashColor = flashColor;
        flash.Remaining = flash.Duration;
    }

    public void Dispose()
    {
        _collisions.Clear();
        GC.SuppressFinalize(this);
    }
}

// ─── flash / blink ─────────────────────────────────────────────────────────

/// Drives <see cref="FlashComponent"/>: every frame it repaints a mesh entity's
/// vertex colors to either its resting <c>BaseColor</c> (when idle) or a value
/// lerped from <c>FlashColor</c> back toward <c>BaseColor</c> while a blink decays.
/// Writes colors in place (no allocation), so it must run in the update pipeline
/// before the mesh prep / render stage reads the vertices.
public class FlashSystem(World world)
    : AEntitySetSystem<GameState>(world.GetEntities().With<FlashComponent>().With<DrawComponent>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var flash = ref entity.Get<FlashComponent>();
        var draw = entity.Get<DrawComponent>();
        if (draw.Vertices is not { Length: > 0 } vertices) return;

        Color color;
        if (flash.Remaining > 0f)
        {
            flash.Remaining = MathF.Max(0f, flash.Remaining - state.Time);
            var t = flash.Duration > 0f ? flash.Remaining / flash.Duration : 0f;
            color = Color.Lerp(flash.BaseColor, flash.FlashColor, t);
        }
        else
        {
            color = flash.BaseColor;
        }

        for (var i = 0; i < vertices.Length; i++)
            vertices[i].Color = color;
    }
}

// ─── speed clamp ───────────────────────────────────────────────────────────

/// Caps each ball's velocity magnitude. The floor boost adds fixed energy on every
/// bounce (the demo is "self-sustaining, not damped"), which otherwise compounds
/// until a ball is fast enough to tunnel through a wall in a single step. Clamping
/// the speed bounds the energy and keeps the balls contained. Runs after the bounce
/// system (the last writer of ball velocity) so the cap is the final word.
public class BallSpeedClampSystem(World world, float maxSpeed)
    : AEntitySetSystem<GameState>(world.GetEntities().With<BallTagComponent>().With<VelocityComponent>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var velocity = ref entity.Get<VelocityComponent>();
        var speedSq = velocity.Current.LengthSquared();
        if (speedSq > maxSpeed * maxSpeed)
            velocity.Current = velocity.Current * (maxSpeed / MathF.Sqrt(speedSq));
    }
}

// ─── resting / sleep ───────────────────────────────────────────────────────

/// Lets a settled ball stop being integrated so dense piles go truly still. With
/// gravity on, every ball is nudged ~g·dt downward each frame and moved into its
/// contact *before* detection runs, so the resolver is forever correcting a sink it
/// can't prevent — that residual sink↔correct cycle is the "resting jitter" (and the
/// reason a small overlap lingers instead of reaching zero). This system breaks the
/// cycle: once a ball's speed stays below SleepSpeed for SleepDelay it is marked
/// asleep, and thereafter its velocity is zeroed here — right after gravity, before
/// integration — cancelling the gravity nudge so it neither sinks nor jitters. The
/// bounce system's position correction still runs, so a freshly-slept ball relaxes
/// the last of its overlap to tangent and then quiesces against the slop deadband.
/// Balls wake on a deep incursion (<see cref="BallBounceSystem"/>) or any demo toggle
/// (<see cref="PhysicsDemoScreen.WakeAllBalls"/>). Gated on GravityEnabled so it acts
/// only in the gravity-on settling case — never while paused, and never in the lively
/// floor-boost showcase, where balls never slow enough to sleep anyway.
public class BallRestSystem : ISystem<GameState>
{
    private const float SleepSpeed = 24f;   // speed (post-gravity) under which a ball accrues still-time
    private const float SleepDelay = 0.4f;  // seconds of continuous stillness before sleeping

    private readonly EntitySet _balls;
    private readonly PhysicsDemoScreen _screen;
    public bool IsEnabled { get; set; } = true;

    public BallRestSystem(World world, PhysicsDemoScreen screen)
    {
        _screen = screen;
        _balls = world.GetEntities().With<BallTagComponent>().With<VelocityComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled || !_screen.GravityEnabled) return;

        foreach (var entity in _balls.GetEntities())
        {
            ref var tag = ref entity.Get<BallTagComponent>();
            var velocity = entity.Get<VelocityComponent>();

            if (tag.Asleep)
            {
                velocity.Current = Vector2.Zero;   // cancel the gravity just applied → frozen
                continue;
            }

            if (velocity.Current.LengthSquared() < SleepSpeed * SleepSpeed)
            {
                tag.StillTime += state.Time;
                if (tag.StillTime >= SleepDelay)
                {
                    tag.Asleep = true;
                    velocity.Current = Vector2.Zero;
                }
            }
            else
            {
                tag.StillTime = 0f;
            }
        }
    }

    public void Dispose()
    {
        _balls.Dispose();
        GC.SuppressFinalize(this);
    }
}
