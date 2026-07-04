#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Draws the <b>Edit-only tinted outlines</b> for trigger zones (island-authoring plan §5.3) plus
/// the armed-trigger placement ghost. A trigger is a <c>Passive</c> box collider with no sprite —
/// invisible in Play, and it would be invisible in Edit too (nothing renders a passive collider),
/// so this pass gives it a visible, clickable outline: without it a designer could neither see nor
/// select a placed zone. It is the trigger analogue of the gizmo/proxy overlay pass — screen-baked
/// on the native-resolution Editor target through <see cref="OverlayProjection"/>, standalone
/// entities tagged <see cref="EditorInfrastructureComponent"/>, no <c>VisibleComponent</c> (the
/// chrome rule).
///
/// <para><b>What counts as a trigger.</b> An entity with a <c>Passive</c>
/// <c>BoxColliderComponent</c>, a <see cref="SceneObjectComponent"/>, and no
/// <c>SpriteInfoComponent</c> (footprints ride a sprite; boundary bake products are convex + carry
/// <see cref="BakedProductComponent"/>). No new marker component — the trigger IS a passive
/// collider + an <c>EntityInfoComponent</c> identity, per the plan.</para>
///
/// <para><b>Ghost.</b> While the palette has a trigger type armed (read through
/// <c>armedTriggerProvider</c>), a preview box of that type's size follows the snapped cursor, so
/// placement is not blind. Woven into the DRAW pipeline's <c>editor.overlayPrep</c> pass; Edit-only
/// (cleared in Play).</para>
/// </summary>
public sealed class TriggerOverlaySystem : ISystem<GameState>
{
    /// <summary>The trigger outline color (amber — distinct from proxy cyan, gizmo yellow, boundary aqua).</summary>
    public static readonly Color OutlineColor = new(255, 196, 64);

    /// <summary>The armed-trigger ghost color (the outline tint, dimmed).</summary>
    public static readonly Color GhostColor = new Color(255, 196, 64) * 0.7f;

    /// <summary>Outline stroke thickness in virtual pixels (aspect-fit scaled to screen).</summary>
    public const float OutlinePixelThickness = 2f;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly Func<TriggerType?>? _armedTriggerProvider;

    private readonly EntitySet _triggerSet;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _gizmoStateSet;

    private readonly Dictionary<Entity, Entity> _outlines = new();
    private readonly List<Entity> _stale = new();
    private Entity _ghost;
    private bool _ghostAlive;

    public bool IsEnabled { get; set; } = true;

    /// <param name="armedTriggerProvider">Returns the palette's currently-armed trigger type (null
    /// when none / no palette) — used to draw the placement ghost. Optional.</param>
    public TriggerOverlaySystem(
        World world,
        Camera camera,
        ViewportManager? viewportManager = null,
        Func<TriggerType?>? armedTriggerProvider = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _viewportManager = viewportManager;
        _armedTriggerProvider = armedTriggerProvider;
        _triggerSet = world.GetEntities()
            .With<BoxColliderComponent>().With<SceneObjectComponent>().With<TransformComponent>()
            .Without<SpriteInfoComponent>().AsSet();
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
    }

    public void Update(GameState state) { /* draw-phase only — see EmitOverlays */ }

    /// <summary>Bakes the trigger VISUALS for this frame (Edit only). Called from the DRAW
    /// pipeline's <c>editor.overlayPrep</c> pass, so it reads the frame's final camera.</summary>
    public void EmitOverlays(GameState state)
    {
        if (!IsEnabled || state.RunMode != RunMode.Edit)
        {
            DespawnAll();
            return;
        }

        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        var thickness = projection.ToScreenSize(OutlinePixelThickness);

        var live = new HashSet<Entity>();
        foreach (var trigger in _triggerSet.GetEntities())
        {
            if (!trigger.IsAlive) continue;
            var box = trigger.Get<BoxColliderComponent>();
            if (!box.Passive) continue; // physical footprints are not triggers
            live.Add(trigger);
            var corners = ProxyGeometry.BoxWorldCorners(trigger.Get<TransformComponent>(), box);
            EmitBox(EnsureOutline(trigger), corners, thickness, OutlineColor, projection);
        }

        _stale.Clear();
        foreach (var kv in _outlines)
            if (!live.Contains(kv.Key)) _stale.Add(kv.Key);
        foreach (var dead in _stale)
        {
            if (_outlines[dead].IsAlive) _outlines[dead].Dispose();
            _outlines.Remove(dead);
        }

        EmitGhost(projection, thickness);
    }

    private void EmitGhost(in OverlayProjection projection, float thickness)
    {
        var armed = _armedTriggerProvider?.Invoke();
        if (armed is not { } type || !TryGetCursorWorld(out var cursorWorld))
        {
            DespawnGhost();
            return;
        }

        var centre = SnapPoint(cursorWorld);
        var half = type.Size / 2f;
        var corners = new[]
        {
            centre + new Vector2(-half.X, -half.Y), centre + new Vector2(half.X, -half.Y),
            centre + new Vector2(half.X, half.Y), centre + new Vector2(-half.X, half.Y),
        };
        EnsureGhost();
        EmitBox(_ghost, corners, thickness, GhostColor, projection);
    }

    private void EmitBox(Entity entity, Vector2[] worldCorners, float thickness, Color color,
        in OverlayProjection projection)
    {
        var screen = new Vector2[worldCorners.Length];
        for (var i = 0; i < worldCorners.Length; i++) screen[i] = projection.ToScreen(worldCorners[i]);
        var mesh = OverlayMeshClip.ClipToRect(
            new PolygonOutlineMeshGenerator(screen, thickness, color, closed: true).Generate(),
            projection.Viewport);
        ref var draw = ref entity.Get<DrawComponent>();
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
    }

    private Entity EnsureOutline(Entity trigger)
    {
        if (_outlines.TryGetValue(trigger, out var e) && e.IsAlive) return e;
        e = CreateOverlayEntity();
        _outlines[trigger] = e;
        return e;
    }

    private void EnsureGhost()
    {
        if (_ghostAlive && _ghost.IsAlive) return;
        _ghost = CreateOverlayEntity();
        _ghostAlive = true;
    }

    private Entity CreateOverlayEntity()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        e.Set(new TransformComponent());
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = ProxySyncSystem.ProxyLayerDepth,
            WorldMatrix = Matrix.Identity,
        });
        return e;
    }

    private void DespawnGhost()
    {
        if (_ghostAlive && _ghost.IsAlive) _ghost.Dispose();
        _ghostAlive = false;
    }

    private void DespawnAll()
    {
        foreach (var e in _outlines.Values)
            if (e.IsAlive) e.Dispose();
        _outlines.Clear();
        DespawnGhost();
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

    private Vector2 SnapPoint(Vector2 world)
    {
        foreach (var e in _gizmoStateSet.GetEntities())
        {
            ref readonly var gizmo = ref e.Get<GizmoStateComponent>();
            return gizmo.SnapEnabled && gizmo.GridStep > 0f
                ? GizmoTransform.Snap(world, gizmo.GridStep)
                : world;
        }
        return world;
    }

    public void Dispose()
    {
        DespawnAll();
        _triggerSet.Dispose();
        _cursorSet.Dispose();
        _gizmoStateSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
