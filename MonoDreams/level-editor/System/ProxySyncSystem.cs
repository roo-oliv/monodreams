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
/// <para><b>Overlay rules (unchanged from the gizmo).</b> Proxies are never
/// <c>ChildOf</c>-parented (<c>HierarchySystem.DisposeOrphans</c> is live in Edit), set
/// <c>VisibleComponent</c> themselves (<c>CullingSystem</c> only visits sprite entities), and
/// draw world-space on <c>Main</c> with <c>1/Camera.Zoom</c>-scaled line thickness. Their mesh
/// vertices are baked <b>relative to the proxy's transform position</b> (unlike the gizmo's
/// world-baked, identity-transform overlays) because the proxy transform must sit at the shape's
/// centre for the gizmo pivot — <c>MeshPrepSystem</c> then places the mesh through that same
/// transform, so the two never double-offset.</para>
///
/// <para><b>Edit-guarded, registered RunNormally</b> (entry <c>editor.proxySync</c>), woven
/// after <c>editor.gizmo</c> so the same frame's write-back is what the proxies re-derive from,
/// and before <c>HierarchySystem</c> like the rest of the edit block.</para>
/// </summary>
public sealed class ProxySyncSystem : ISystem<GameState>
{
    /// <summary>Proxy outlines draw just under the gizmo overlay (0.999), over game sprites.</summary>
    public const float ProxyLayerDepth = 0.998f;

    /// <summary>On-screen outline thickness in pixels (divided by zoom for world units) —
    /// deliberately thicker than the debug outline's 0.5 so the interactive shape reads as such.</summary>
    public const float OutlinePixelThickness = 2f;

    /// <summary>The proxy outline color — distinct from the debug outlines (red/green/gray) and
    /// the gizmo's selection yellow.</summary>
    public static readonly Color OutlineColor = Color.Cyan;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly EntitySet _selectedSet;

    // Owned proxy entities, one per collider kind on the current anchor (created/disposed as the
    // anchor's component set changes; rebuilt each frame while alive).
    private Entity _anchor;
    private Entity _boxProxy;
    private Entity _convexProxy;

    public bool IsEnabled { get; set; } = true;

    public ProxySyncSystem(World world, Camera camera)
    {
        _world = world;
        _camera = camera;
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

        var invZoom = _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;
        SyncProxy(ref _boxProxy, anchor, ProxyBindingKind.BoxColliderBounds,
            anchor.Has<BoxColliderComponent>(), invZoom);
        SyncProxy(ref _convexProxy, anchor, ProxyBindingKind.ConvexColliderShape,
            anchor.Has<ConvexColliderComponent>(), invZoom);

        // Physics is frozen in Edit, so nothing refreshes the anchor's convex WorldVertices while
        // the designer moves it — keep them (and thus the red debug outline) coherent here.
        if (anchor.Has<ConvexColliderComponent>())
            anchor.Get<ConvexColliderComponent>().UpdateWorldVertices(anchor.Get<TransformComponent>());
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

    private void SyncProxy(ref Entity proxy, Entity anchor, ProxyBindingKind kind, bool shouldExist, float invZoom)
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

        // Place the proxy's transform at the shape's world centre (the gizmo pivot), and bake the
        // outline mesh relative to it — MeshPrepSystem translates the mesh through this same
        // transform, so placement and visual can never diverge.
        var center = ProxyGeometry.Centroid(outline);
        proxy.Get<TransformComponent>().Position = center;

        var local = new Vector2[outline.Length];
        for (var i = 0; i < outline.Length; i++) local[i] = outline[i] - center;
        var mesh = new PolygonOutlineMeshGenerator(
            local, OutlinePixelThickness * invZoom, OutlineColor, closed: true).Generate();

        ref var draw = ref proxy.Get<DrawComponent>();
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
    }

    private Entity CreateProxyEntity(Entity anchor, ProxyBindingKind kind)
    {
        // Standalone (never ChildOf-parented — DisposeOrphans is live in Edit), self-visible
        // (CullingSystem only visits sprite entities), world-space on Main.
        var proxy = _world.CreateEntity();
        proxy.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        proxy.Set(new GizmoProxyComponent(anchor, kind));
        proxy.Set(new TransformComponent());
        proxy.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            LayerDepth = ProxyLayerDepth,
            WorldMatrix = Matrix.Identity,
        });
        proxy.Set(new VisibleComponent());
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
