#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Materializes and maintains the <b>edit-time gizmo proxies</b> (Wave 8b): when the selected
/// entity in <see cref="RunMode.Edit"/> carries collider components — whose shapes are
/// component-local spatial data, not entities — this system spawns one standalone proxy entity
/// per collider (<see cref="GizmoProxyComponent"/> binding, <c>TransformComponent</c> at the
/// shape's world centre, a distinct outline mesh, self-set <c>VisibleComponent</c>) so the shape
/// becomes selectable and draggable through the ordinary selection + gizmo path. Proxies are
/// re-derived from the bound component <b>every frame</b> (cheap — only the selected entity's
/// colliders), so they track both the owner's transform edits and the gizmo's collider
/// write-backs live; they despawn on deselect, mode exit, or target death.
///
/// <para><b>Trigger = selection, not a mode toggle.</b> The proxies appear exactly while the
/// owning entity (or one of its own proxies — clicking a proxy keeps the family alive) is
/// selected. No separate "collider edit mode": the selection already scopes the proxies to one
/// entity, so there is nothing else to toggle, and the affordance sits where the designer's
/// attention already is.</para>
///
/// <para><b>Coexistence with <c>ColliderDebugSystem</c>.</b> The debug system stays the global
/// diagnostic: red/green/gray thin outlines for EVERY collider, behind its own static flag,
/// unaware of selection. The proxy is the <b>edit affordance</b>: a thicker cyan outline for the
/// SELECTED entity only, independent of the debug flag (collider editing must not require the
/// debug overlay). They render at different depths (proxy 0.998 under the gizmo's 0.999; debug
/// at 1.0) and the sync keeps the selected entity's convex <c>WorldVertices</c> fresh (physics
/// is frozen in Edit), so the two outlines stay coherent instead of drifting apart.</para>
///
/// <para><b>Overlay rules (matching the gizmo's).</b> Proxies are never <c>ChildOf</c>-parented
/// (<c>HierarchySystem.DisposeOrphans</c> is live in Edit). Their TRANSFORM stays real — placed
/// at the shape's <b>world</b> centre every update frame, because it is the gizmo's drag pivot
/// and the selection's world anchor. Their VISUAL, however, is emitted by
/// <see cref="EmitOverlays"/> (called from the draw pipeline's <c>editor.overlayPrep</c> entry,
/// after the camera is final) in <b>screen pixels</b> on the native-resolution
/// <c>RenderTargetID.Editor</c> target: the world outline is projected through the pure
/// <c>OverlayProjection</c>, stroked at a constant on-screen thickness (aspect-fit scaled, never
/// zoom-compensated — same apparent size as the old <c>1/Camera.Zoom</c> math, rasterized
/// natively), and clipped to the game viewport rectangle (<c>OverlayMeshClip</c>). Per the chrome
/// rule the proxies carry <b>no</b> <c>VisibleComponent</c> (the Editor pass renders every
/// matching entity, and its presence would pull the mesh into <c>MeshPrepSystem</c>, which would
/// overwrite the identity <c>WorldMatrix</c> the screen-baked vertices require — the transform
/// placement and the visual are now decoupled by design: both re-derive from the same collider
/// every frame, so they cannot diverge).</para>
///
/// <para><b>Edit-guarded, registered RunNormally</b> (entry <c>editor.proxySync</c>), woven
/// after <c>editor.gizmo</c> so the same frame's write-back is what the proxies re-derive from,
/// and before <c>HierarchySystem</c> like the rest of the edit block.</para>
/// </summary>
public sealed class ProxySyncSystem : ISystem<GameState>
{
    /// <summary>The proxy outlines' depth band on the Editor target: just under the gizmo
    /// overlays (<see cref="GizmoSystem.OverlayLayerDepth"/> = 0.04) and under the shell's opaque
    /// panels (<c>EditorChromeBuilder.PanelDepth</c> = 0.1), which therefore cover them over the
    /// chrome margins. (The SELECTION pick still ranks proxies by
    /// <c>SelectionSystem.ProxyBorderPickDepth</c> — a constant decoupled from this visual
    /// depth.)</summary>
    public const float ProxyLayerDepth = 0.02f;

    /// <summary>Outline thickness in VIRTUAL pixels (aspect-fit scaled to screen pixels by the
    /// emission — never zoom-compensated) — deliberately thicker than the debug outline's 0.5 so
    /// the interactive shape reads as such.</summary>
    public const float OutlinePixelThickness = 2f;

    /// <summary>The proxy outline color — distinct from the debug outlines (red/green/gray) and
    /// the gizmo's selection yellow.</summary>
    public static readonly Color OutlineColor = Color.Cyan;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly EntitySet _selectedSet;

    // Owned proxy entities, one per collider kind on the current anchor (created/disposed as the
    // anchor's component set changes; rebuilt each frame while alive).
    private Entity _anchor;
    private Entity _boxProxy;
    private Entity _convexProxy;

    public bool IsEnabled { get; set; } = true;

    /// <param name="viewportManager">Supplies the aspect-fit destination the outline visuals are
    /// projected into (see <c>OverlayProjection</c>). Null (world-free unit tests) degrades to
    /// the identity aspect-fit — screen == virtual.</param>
    public ProxySyncSystem(World world, Camera camera, ViewportManager? viewportManager = null)
    {
        _world = world;
        _camera = camera;
        _viewportManager = viewportManager;
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Edit-guarded: inert in Play; tear the proxies down so they don't linger after editing.
        if (state.RunMode != RunMode.Edit)
        {
            DespawnAll();
            return;
        }

        var anchor = ResolveAnchor();
        if (anchor == default || !anchor.IsAlive || !anchor.Has<TransformComponent>())
        {
            DespawnAll();
            return;
        }

        if (anchor != _anchor)
        {
            DespawnAll();
            _anchor = anchor;
        }

        SyncProxy(ref _boxProxy, anchor, ProxyBindingKind.BoxColliderBounds,
            anchor.Has<BoxColliderComponent>());
        SyncProxy(ref _convexProxy, anchor, ProxyBindingKind.ConvexColliderShape,
            anchor.Has<ConvexColliderComponent>());

        // Physics is frozen in Edit, so nothing refreshes the anchor's convex WorldVertices while
        // the designer moves it — keep them (and thus the red debug outline) coherent here.
        if (anchor.Has<ConvexColliderComponent>())
            anchor.Get<ConvexColliderComponent>().UpdateWorldVertices(anchor.Get<TransformComponent>());
    }

    /// <summary>
    /// Emits (or clears) the proxies' outline VISUALS for this frame, in screen pixels on the
    /// native-resolution Editor target. Called from the DRAW pipeline (the
    /// <c>editor.overlayPrep</c> entry) after the camera is final, so the outlines never lag a
    /// camera pan/zoom. Each live proxy's world outline is re-derived from the bound collider
    /// (the same <see cref="ProxyGeometry.TryGetWorldOutline"/> the update-side placement used),
    /// projected, stroked, and clipped to the game viewport rectangle.
    /// </summary>
    public void EmitOverlays(GameState state)
    {
        if (!IsEnabled || state.RunMode != RunMode.Edit) return;
        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        EmitProxyOutline(_boxProxy, projection);
        EmitProxyOutline(_convexProxy, projection);
    }

    private static void EmitProxyOutline(Entity proxy, in OverlayProjection projection)
    {
        if (!proxy.IsAlive) return;
        var binding = proxy.Get<GizmoProxyComponent>();
        if (!ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, out var outline)) return;

        var points = new Vector2[outline.Length];
        for (var i = 0; i < outline.Length; i++) points[i] = projection.ToScreen(outline[i]);
        var mesh = OverlayMeshClip.ClipToRect(
            new PolygonOutlineMeshGenerator(
                points, projection.ToScreenSize(OutlinePixelThickness), OutlineColor, closed: true).Generate(),
            projection.Viewport);

        ref var draw = ref proxy.Get<DrawComponent>();
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
    }

    /// <summary>
    /// The entity whose colliders are proxied: the selected entity itself, or — when the
    /// selection IS a proxy (the designer clicked one) — that proxy's bound target, so selecting
    /// a proxy never despawns the family it belongs to.
    /// </summary>
    private Entity ResolveAnchor()
    {
        foreach (var selected in _selectedSet.GetEntities())
        {
            if (!selected.IsAlive) continue;
            if (selected.Has<GizmoProxyComponent>())
            {
                var target = selected.Get<GizmoProxyComponent>().Target;
                return target.IsAlive ? target : default;
            }
            return selected; // single-select: the first live selected entity is the one
        }
        return default;
    }

    private void SyncProxy(ref Entity proxy, Entity anchor, ProxyBindingKind kind, bool shouldExist)
    {
        if (!shouldExist)
        {
            if (proxy.IsAlive) proxy.Dispose();
            proxy = default;
            return;
        }

        if (!proxy.IsAlive)
            proxy = CreateProxyEntity(anchor, kind);

        if (!ProxyGeometry.TryGetWorldOutline(anchor, kind, out var outline))
        {
            proxy.Dispose();
            proxy = default;
            return;
        }

        // Place the proxy's transform at the shape's WORLD centre — the gizmo's drag pivot and
        // the selection's anchor. The outline VISUAL is emitted separately (EmitOverlays, draw
        // phase, screen space); both re-derive from the same collider, so they cannot diverge.
        proxy.Get<TransformComponent>().Position = ProxyGeometry.Centroid(outline);
    }

    private Entity CreateProxyEntity(Entity anchor, ProxyBindingKind kind)
    {
        // Standalone (never ChildOf-parented — DisposeOrphans is live in Edit). The visual is a
        // screen-baked mesh on the native-resolution Editor target; NO VisibleComponent per the
        // chrome rule (the Editor pass renders every matching entity, and its presence would pull
        // the mesh into MeshPrepSystem, which overwrites the identity WorldMatrix the
        // screen-baked vertices require).
        var proxy = _world.CreateEntity();
        proxy.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        proxy.Set(new GizmoProxyComponent(anchor, kind));
        proxy.Set(new TransformComponent());
        proxy.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = ProxyLayerDepth,
            WorldMatrix = Matrix.Identity,
        });
        return proxy;
    }

    private void DespawnAll()
    {
        if (_boxProxy.IsAlive) _boxProxy.Dispose();
        if (_convexProxy.IsAlive) _convexProxy.Dispose();
        _boxProxy = default;
        _convexProxy = default;
        _anchor = default;
    }

    public void Dispose()
    {
        DespawnAll();
        _selectedSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
