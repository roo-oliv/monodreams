#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The freeform <b>boundary tool</b> (island-authoring plan §5.2): a new tool modality
/// (<see cref="EditorToolMode.Boundary"/>) in which clicks lay polyline vertices with a live
/// preview line, <b>Enter or a double-click commits</b> the whole lay as ONE undo step, and
/// <b>Escape or right-click cancels</b>. Commit creates an authoring entity carrying a
/// <see cref="BoundaryComponent"/> (pure serialized data) + <see cref="SceneObjectComponent"/>;
/// <c>BoundaryBakeSystem</c> then reacts to the added component and generates the collision.
///
/// <para><b>Boundary points are local to the entity's pivot (the polyline centroid).</b> On commit
/// the tool sets the entity's <c>TransformComponent.Position</c> to the centroid of the laid world
/// points and stores <c>Points</c> relative to it — the same local-space convention convex colliders
/// use, so the bake and the per-vertex proxies (<see cref="ProxyBindingKind.BoundaryVertex"/>) share
/// one frame. After commit, selecting the boundary spawns per-vertex proxies (via
/// <c>ProxySyncSystem</c>) for editing.</para>
///
/// <para><b>Two hooks, like the gizmo.</b> <see cref="Update"/> (UPDATE pipeline, entry
/// <c>editor.boundary</c>) owns the lay lifecycle; <see cref="EmitOverlays"/> (DRAW pipeline, the
/// <c>editor.overlayPrep</c> pass) bakes the VISUALS in screen pixels on the native-resolution
/// Editor target — one outline per committed boundary (Edit-only, so a boundary is visible and
/// clickable — its own polyline is what selection border-picks) plus the in-progress preview line
/// (laid points + a segment to the cursor). Overlay entities are standalone, carry
/// <see cref="EditorInfrastructureComponent"/>, and never a <c>VisibleComponent</c> (the chrome
/// rule). Edit-guarded — inert and cleared in Play.</para>
///
/// <para><b>Headless-drivable.</b> <see cref="BeginBoundary"/> / <see cref="LayVertex"/> /
/// <see cref="CommitBoundary"/> / <see cref="CancelBoundary"/> are public — the overlay's named
/// dispatch routes <c>boundary:begin</c> / <c>boundary:commit</c> / <c>boundary:cancel</c> here, and
/// lay is scripted with cursor ops (a click in Boundary mode lays a vertex).</para>
/// </summary>
public sealed class BoundaryToolSystem : ISystem<GameState>
{
    /// <summary>Frames within which a second left press near the last one reads as a double-click
    /// (commit) rather than a new vertex.</summary>
    public const int DoubleClickFrames = 18;

    /// <summary>The world-unit radius within which a quick second press is a double-click.</summary>
    public const float DoubleClickWorldRadius = 6f;

    /// <summary>Preview/outline stroke thickness in virtual pixels (aspect-fit scaled to screen).</summary>
    public const float OutlinePixelThickness = 2f;

    /// <summary>The committed boundary outline color (distinct from the proxy cyan / gizmo yellow).</summary>
    public static readonly Color OutlineColor = EditorTheme.OverlayBoundary;

    /// <summary>The in-progress lay preview color.</summary>
    public static readonly Color PreviewColor = EditorTheme.OverlayBoundaryPreview;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly EditorHistory _history;
    private readonly SceneSerializer _serializer;
    private readonly Func<GameState, bool>? _commitRequested;
    private readonly Func<GameState, bool>? _cancelRequested;
    private readonly float _thickness;

    private readonly EntitySet _cursorSet;
    private readonly EntitySet _gizmoStateSet;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _boundarySet;

    // In-progress lay state (WORLD points).
    private readonly List<Vector2> _pending = new();
    private int _frame;
    private int _lastPressFrame = int.MinValue;
    private Vector2 _lastPressWorld;

    // Owned overlay entities.
    private readonly Dictionary<Entity, Entity> _outlines = new();
    private readonly List<Entity> _staleOutlines = new();
    private Entity _preview;
    private bool _previewAlive;

    public bool IsEnabled { get; set; } = true;

    /// <param name="viewportManager">The aspect-fit destination for the overlay projection. Null
    /// (world-free unit tests) degrades to identity (screen == virtual) — the lay/commit/cancel
    /// lifecycle still works headlessly.</param>
    /// <param name="commitRequested">Enter-commit predicate (optional; double-click always commits).</param>
    /// <param name="cancelRequested">Escape-cancel predicate (optional; right-click always cancels).</param>
    public BoundaryToolSystem(
        World world,
        Camera camera,
        EditorHistory history,
        SceneSerializer serializer,
        ViewportManager? viewportManager = null,
        Func<GameState, bool>? commitRequested = null,
        Func<GameState, bool>? cancelRequested = null,
        float thickness = BoundaryComponent.DefaultThickness)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _viewportManager = viewportManager;
        _commitRequested = commitRequested;
        _cancelRequested = cancelRequested;
        _thickness = thickness > 0f ? thickness : BoundaryComponent.DefaultThickness;

        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _boundarySet = world.GetEntities().With<BoundaryComponent>().With<TransformComponent>().AsSet();
    }

    /// <summary>The number of vertices laid in the in-progress boundary (0 when not laying).</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Whether a boundary is being laid (Boundary mode with at least one point).</summary>
    public bool IsLaying => GetMode() == EditorToolMode.Boundary && _pending.Count > 0;

    // ---- Public API (interactive input + headless ops route through these) ----

    /// <summary>Enters <see cref="EditorToolMode.Boundary"/> and starts a fresh polyline.</summary>
    public void BeginBoundary()
    {
        _pending.Clear();
        SetMode(EditorToolMode.Boundary);
    }

    /// <summary>Appends a vertex at <paramref name="world"/> (already snapped by the caller). Enters
    /// Boundary mode if not already in it (so a headless lay op works without an explicit begin).</summary>
    public void LayVertex(Vector2 world)
    {
        if (GetMode() != EditorToolMode.Boundary) SetMode(EditorToolMode.Boundary);
        _pending.Add(world);
    }

    /// <summary>Commits the laid polyline as one undo step (creating the boundary authoring entity),
    /// then returns to <see cref="EditorToolMode.SelectTransform"/> and selects the new boundary.
    /// A polyline with fewer than <see cref="BoundaryGeometry.MinPoints"/> vertices is discarded
    /// with a loud warning (there is no edge to bake).</summary>
    public Entity CommitBoundary()
    {
        if (_pending.Count < BoundaryGeometry.MinPoints)
        {
            if (_pending.Count > 0)
                Logger.Warning(
                    $"[level-editor] Boundary commit needs at least {BoundaryGeometry.MinPoints} " +
                    "points — the lay was discarded.");
            CancelBoundary();
            return default;
        }

        var worldPoints = _pending.ToArray();
        var centroid = BoundaryGeometry.Centroid(worldPoints);
        var localPoints = new Vector2[worldPoints.Length];
        for (var i = 0; i < worldPoints.Length; i++) localPoints[i] = worldPoints[i] - centroid;
        var thickness = _thickness;

        var created = default(Entity);
        _history.Push(new CreateEntityCommand(_world, _serializer,
            w =>
            {
                created = w.CreateEntity();
                created.Set(new EntityInfoComponent("Boundary", NextBoundaryName()));
                created.Set(new TransformComponent(centroid));
                // The SceneObjectComponent tag is added by CreateEntityCommand; the bake children
                // are created by BoundaryBakeSystem reacting to this added component.
                created.Set(new BoundaryComponent(localPoints, thickness));
                return created;
            }));

        _pending.Clear();
        SetMode(EditorToolMode.SelectTransform);

        ClearSelection();
        if (created.IsAlive) created.Set(new SelectedComponent());

        Logger.Info($"[level-editor] Committed boundary ({worldPoints.Length} points, " +
                    $"thickness {thickness:0.#}).");
        return created;
    }

    /// <summary>Discards the in-progress lay and returns to <see cref="EditorToolMode.SelectTransform"/>.</summary>
    public void CancelBoundary()
    {
        _pending.Clear();
        SetMode(EditorToolMode.SelectTransform);
    }

    // ---- Frame lifecycle ----

    public void Update(GameState state)
    {
        if (!IsEnabled) { return; }
        _frame++;

        if (state.RunMode != RunMode.Edit)
        {
            _pending.Clear(); // inert in Play
            return;
        }

        // If the designer left Boundary mode mid-lay (picked another tool), discard the lay.
        if (GetMode() != EditorToolMode.Boundary)
        {
            if (_pending.Count > 0) _pending.Clear();
            return;
        }

        // Enter/Escape (screen predicates) and the pointer.
        if (_cancelRequested?.Invoke(state) == true) { CancelBoundary(); return; }
        if (_commitRequested?.Invoke(state) == true) { CommitBoundary(); return; }

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            if (input.RightButtonPressed) { CancelBoundary(); return; }
            if (!input.LeftButtonPressed || input.OutsideViewport) return;

            var world = SnapPoint(input.WorldPosition);
            // Double-click (a quick second press near the last one) commits instead of laying a
            // near-duplicate final vertex.
            if (_pending.Count >= 1
                && _frame - _lastPressFrame <= DoubleClickFrames
                && Vector2.Distance(world, _lastPressWorld) <= DoubleClickWorldRadius)
            {
                CommitBoundary();
                return;
            }

            LayVertex(world);
            _lastPressFrame = _frame;
            _lastPressWorld = world;
            return; // single cursor
        }
    }

    /// <summary>
    /// Bakes the boundary VISUALS for this frame (Edit only): one outline per committed boundary
    /// (so a boundary is visible and its polyline is what selection border-picks) plus the
    /// in-progress lay preview. Called from the DRAW pipeline's <c>editor.overlayPrep</c> pass, so
    /// it reads the frame's final camera. Screen-baked on the native-resolution Editor target.
    /// </summary>
    public void EmitOverlays(GameState state)
    {
        if (!IsEnabled || state.RunMode != RunMode.Edit)
        {
            DespawnAll();
            return;
        }

        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        var thickness = projection.ToScreenSize(OutlinePixelThickness);

        // Committed boundaries: one reused outline entity each.
        var live = new HashSet<Entity>();
        foreach (var boundary in _boundarySet.GetEntities())
        {
            if (!boundary.IsAlive) continue;
            live.Add(boundary);
            var component = boundary.Get<BoundaryComponent>();
            if (component.Points == null || component.Points.Length < 2) continue;
            var worldPoly = BoundaryGeometry.WorldPolyline(
                component.Points, boundary.Get<TransformComponent>().Position);
            EmitPolyline(EnsureOutline(boundary), worldPoly, thickness, OutlineColor, projection, closed: false);
        }

        // Despawn outlines whose boundary died.
        _staleOutlines.Clear();
        foreach (var kv in _outlines)
            if (!live.Contains(kv.Key)) _staleOutlines.Add(kv.Key);
        foreach (var dead in _staleOutlines)
        {
            if (_outlines[dead].IsAlive) _outlines[dead].Dispose();
            _outlines.Remove(dead);
        }

        // In-progress preview: laid points + a segment to the (snapped) cursor.
        if (GetMode() == EditorToolMode.Boundary && _pending.Count >= 1)
        {
            var pts = new List<Vector2>(_pending);
            if (TryGetCursorWorld(out var cursorWorld)) pts.Add(SnapPoint(cursorWorld));
            EnsurePreview();
            EmitPolyline(_preview, pts.ToArray(), thickness, PreviewColor, projection, closed: false);
        }
        else
        {
            DespawnPreview();
        }
    }

    // ---- Overlay emission ----

    private void EmitPolyline(Entity entity, Vector2[] worldPoints, float thickness, Color color,
        in OverlayProjection projection, bool closed)
    {
        if (worldPoints.Length < 2)
        {
            // A single point: nothing to stroke — clear the mesh.
            ref var empty = ref entity.Get<DrawComponent>();
            empty.Vertices = null;
            empty.Indices = null;
            return;
        }

        var screen = new Vector2[worldPoints.Length];
        for (var i = 0; i < worldPoints.Length; i++) screen[i] = projection.ToScreen(worldPoints[i]);
        var mesh = OverlayMeshClip.ClipToRect(
            new PolygonOutlineMeshGenerator(screen, thickness, color, closed).Generate(),
            projection.Viewport);

        ref var draw = ref entity.Get<DrawComponent>();
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
    }

    private Entity EnsureOutline(Entity boundary)
    {
        if (_outlines.TryGetValue(boundary, out var e) && e.IsAlive) return e;
        e = CreateOverlayEntity();
        _outlines[boundary] = e;
        return e;
    }

    private void EnsurePreview()
    {
        if (_previewAlive && _preview.IsAlive) return;
        _preview = CreateOverlayEntity();
        _previewAlive = true;
    }

    private Entity CreateOverlayEntity()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        e.Set(new TransformComponent()); // identity — vertices are baked in screen space
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = ProxySyncSystem.ProxyLayerDepth,
            WorldMatrix = Matrix.Identity,
        });
        // NO VisibleComponent — the chrome rule (see GizmoSystem / ProxySyncSystem).
        return e;
    }

    private void DespawnPreview()
    {
        if (_previewAlive && _preview.IsAlive) _preview.Dispose();
        _previewAlive = false;
    }

    private void DespawnAll()
    {
        foreach (var e in _outlines.Values)
            if (e.IsAlive) e.Dispose();
        _outlines.Clear();
        DespawnPreview();
    }

    // ---- Shared plumbing ----

    private Vector2 SnapPoint(Vector2 world)
    {
        ref readonly var gizmo = ref GetGizmoStateEntity().Get<GizmoStateComponent>();
        return gizmo.SnapEnabled && gizmo.GridStep > 0f
            ? GizmoTransform.Snap(world, gizmo.GridStep)
            : world;
    }

    private bool TryGetCursorWorld(out Vector2 world)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            if (input.OutsideViewport) { world = default; return false; }
            world = input.WorldPosition;
            return true;
        }
        world = default;
        return false;
    }

    private void ClearSelection()
    {
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            (toClear ??= new List<Entity>()).Add(e);
        if (toClear == null) return;
        foreach (var e in toClear)
            if (e.IsAlive && e.Has<SelectedComponent>())
                e.Remove<SelectedComponent>();
    }

    private string NextBoundaryName()
    {
        var max = 0;
        foreach (var boundary in _boundarySet.GetEntities())
            if (boundary.IsAlive && boundary.Has<EntityInfoComponent>())
            {
                var name = boundary.Get<EntityInfoComponent>().Name;
                if (TryParseSuffix(name, "boundary_", out var n) && n > max) max = n;
            }
        return $"boundary_{max + 1:00}";
    }

    internal static bool TryParseSuffix(string? name, string prefix, out int number)
    {
        number = 0;
        if (name == null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(name.Substring(prefix.Length), out number);
    }

    private EditorToolMode GetMode() => GetGizmoStateEntity().Get<GizmoStateComponent>().Mode;

    private void SetMode(EditorToolMode mode)
    {
        ref var state = ref GetGizmoStateEntity().Get<GizmoStateComponent>();
        state.Mode = mode;
    }

    private Entity GetGizmoStateEntity()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return e;
        var created = _world.CreateEntity();
        created.Set(new EditorInfrastructureComponent());
        created.Set(GizmoStateComponent.Default);
        return created;
    }

    public void Dispose()
    {
        DespawnAll();
        _cursorSet.Dispose();
        _gizmoStateSet.Dispose();
        _selectedSet.Dispose();
        _boundarySet.Dispose();
        GC.SuppressFinalize(this);
    }
}
