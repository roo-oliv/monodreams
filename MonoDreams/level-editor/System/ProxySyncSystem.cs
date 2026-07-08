#nullable enable
using System;
using System.Collections.Generic;
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
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Materializes and maintains the <b>edit-time gizmo proxies</b> (Wave 8b, generalized in
/// island-authoring Slice 2): when the selected entity in <see cref="RunMode.Edit"/> carries
/// collider components — whose shapes are component-local spatial data, not entities — this
/// system spawns standalone proxy entities (<see cref="GizmoProxyComponent"/> binding,
/// <c>TransformComponent</c> at the shape's world centre, a distinct outline mesh) so the shape
/// becomes selectable and draggable through the ordinary selection + gizmo path. The family is a
/// <b>(kind, index) list</b> — one whole-shape proxy per collider (index 0) plus, while the
/// convex family's own proxy is selected, one <see cref="ProxyBindingKind.ConvexVertex"/> proxy
/// per <c>ModelVertices</c> entry (index = the vertex ordinal; the same keying a future spline
/// control point uses). Proxies are re-derived from the bound component <b>every frame</b>
/// (cheap — only the selected entity's colliders), so they track the owner's transform edits,
/// the gizmo's collider write-backs, and vertex-count changes (add/delete vertex, undo/redo)
/// live; they despawn on deselect, mode exit, or target death.
///
/// <para><b>Trigger = selection, not a mode toggle.</b> The whole-shape proxies appear exactly
/// while the owning entity (or one of its own proxies — clicking a proxy keeps the family
/// alive) is selected. The per-vertex handles appear one click deeper — while the convex
/// SHAPE proxy (or one of its vertex proxies) is selected — so selecting an entity shows its
/// collider outlines without vertex-handle clutter, and clicking the convex outline opens the
/// vertex-editing session (the Godot/Unity collision-shape convention).</para>
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
    /// panels (<c>EditorTheme.Depths.Panel</c> = 0.1), which therefore cover them over the
    /// chrome margins. (The SELECTION pick still ranks proxies by
    /// <c>SelectionSystem.ProxyBorderPickDepth</c> — a constant decoupled from this visual
    /// depth.)</summary>
    public const float ProxyLayerDepth = EditorTheme.Depths.ProxyOverlay;

    /// <summary>Outline thickness in VIRTUAL pixels (aspect-fit scaled to screen pixels by the
    /// emission — never zoom-compensated) — deliberately thicker than the debug outline's 0.5 so
    /// the interactive shape reads as such.</summary>
    public const float OutlinePixelThickness = 2f;

    /// <summary>The on-screen half-size (VIRTUAL pixels, aspect-fit scaled) of a
    /// <see cref="ProxyBindingKind.ConvexVertex"/> handle's square visual — constant across
    /// camera zoom like every overlay size (the pick anchor is the tiny world-space square in
    /// <c>ProxyGeometry</c>; this is only how big the handle LOOKS).</summary>
    public const float VertexHandlePixelHalfSize = 4f;

    /// <summary>The proxy outline color — distinct from the debug outlines (red/green/gray) and
    /// the gizmo's selection yellow.</summary>
    public static readonly Color OutlineColor = EditorTheme.OverlayAccent;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly EntitySet _selectedSet;

    // Owned proxy entities, keyed (kind, index) — the Slice-2 generalization of the old
    // one-field-per-kind pair (created/disposed as the anchor's component set and vertex count
    // change; re-placed each frame while alive).
    private Entity _anchor;
    private readonly Dictionary<(ProxyBindingKind Kind, int Index), Entity> _proxies = new();
    private readonly List<(ProxyBindingKind Kind, int Index)> _stale = new();

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

        var anchor = ResolveAnchor(out var convexFamilySelected);
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

        var convexCount = 0;
        if (anchor.Has<ConvexColliderComponent>())
            convexCount = anchor.Get<ConvexColliderComponent>().ModelVertices?.Length ?? 0;

        SyncProxy(anchor, ProxyBindingKind.BoxColliderBounds, 0,
            anchor.Has<BoxColliderComponent>());
        SyncProxy(anchor, ProxyBindingKind.ConvexColliderShape, 0, convexCount >= 3);

        // Per-vertex handles: only while the convex family's own proxy (shape or vertex) is
        // selected — one proxy per ModelVertices entry, re-keyed each frame so add/delete-vertex
        // (and their undo/redo) grow/shrink the family live.
        var vertexCount = convexFamilySelected && convexCount >= 3 ? convexCount : 0;
        for (var i = 0; i < vertexCount; i++)
            SyncProxy(anchor, ProxyBindingKind.ConvexVertex, i, true);

        // Boundary vertex handles: a boundary IS its points, so the handles materialize on PLAIN
        // selection of the boundary entity (no shape proxy to click through first). One proxy per
        // BoundaryComponent.Points entry, re-keyed each frame so lay/add/delete/undo resize the
        // family live.
        var boundaryCount = 0;
        if (anchor.Has<BoundaryComponent>())
            boundaryCount = anchor.Get<BoundaryComponent>().Points?.Length ?? 0;
        for (var i = 0; i < boundaryCount; i++)
            SyncProxy(anchor, ProxyBindingKind.BoundaryVertex, i, true);

        // The single thickness handle (Slice 4): rides the band edge; exists while the boundary has
        // at least one edge (>= MinPoints). Like the vertex handles it shows on plain selection.
        SyncProxy(anchor, ProxyBindingKind.BoundaryThickness, 0,
            boundaryCount >= Boundary.BoundaryGeometry.MinPoints);

        _stale.Clear();
        foreach (var key in _proxies.Keys)
        {
            if (key.Kind == ProxyBindingKind.ConvexVertex && key.Index >= vertexCount)
                _stale.Add(key);
            else if (key.Kind == ProxyBindingKind.BoundaryVertex && key.Index >= boundaryCount)
                _stale.Add(key);
        }
        foreach (var key in _stale)
        {
            if (_proxies[key].IsAlive) _proxies[key].Dispose();
            _proxies.Remove(key);
        }

        // Physics is frozen in Edit, so nothing refreshes the anchor's convex WorldVertices while
        // the designer moves it — keep them (and thus the red debug outline) coherent here.
        if (anchor.Has<ConvexColliderComponent>() && convexCount > 0)
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
        foreach (var proxy in _proxies.Values)
            EmitProxyOutline(proxy, projection);
    }

    private static void EmitProxyOutline(Entity proxy, in OverlayProjection projection)
    {
        if (!proxy.IsAlive) return;
        var binding = proxy.Get<GizmoProxyComponent>();
        if (!ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, binding.Index, out var outline)) return;

        Vector2[] points;
        if (ProxyGeometry.IsVertexHandle(binding.Kind))
        {
            // A vertex handle draws as a constant-on-screen square around the projected vertex
            // (the world-space outline square only anchors the pick — projected raw it would be
            // sub-pixel at low zoom).
            var centre = projection.ToScreen(ProxyGeometry.Centroid(outline));
            var half = projection.ToScreenSize(VertexHandlePixelHalfSize);
            points = new[]
            {
                centre + new Vector2(-half, -half), centre + new Vector2(half, -half),
                centre + new Vector2(half, half), centre + new Vector2(-half, half),
            };
        }
        else
        {
            points = new Vector2[outline.Length];
            for (var i = 0; i < outline.Length; i++) points[i] = projection.ToScreen(outline[i]);
        }

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
    /// a proxy never despawns the family it belongs to. <paramref name="convexFamilySelected"/>
    /// reports whether the selection is a convex-family proxy (shape or vertex) — the trigger
    /// for materializing the per-vertex handles.
    /// </summary>
    private Entity ResolveAnchor(out bool convexFamilySelected)
    {
        convexFamilySelected = false;
        foreach (var selected in _selectedSet.GetEntities())
        {
            if (!selected.IsAlive) continue;
            if (selected.Has<GizmoProxyComponent>())
            {
                var binding = selected.Get<GizmoProxyComponent>();
                convexFamilySelected = binding.Kind is ProxyBindingKind.ConvexColliderShape
                    or ProxyBindingKind.ConvexVertex;
                return binding.Target.IsAlive ? binding.Target : default;
            }
            return selected; // single-select: the first live selected entity is the one
        }
        return default;
    }

    private void SyncProxy(Entity anchor, ProxyBindingKind kind, int index, bool shouldExist)
    {
        var key = (kind, index);
        var exists = _proxies.TryGetValue(key, out var proxy) && proxy.IsAlive;

        if (!shouldExist)
        {
            if (exists) proxy.Dispose();
            _proxies.Remove(key);
            return;
        }

        if (!exists)
            _proxies[key] = proxy = CreateProxyEntity(anchor, kind, index);

        if (!ProxyGeometry.TryGetWorldOutline(anchor, kind, index, out var outline))
        {
            proxy.Dispose();
            _proxies.Remove(key);
            return;
        }

        // Place the proxy's transform at the shape's WORLD centre — the gizmo's drag pivot and
        // the selection's anchor. The outline VISUAL is emitted separately (EmitOverlays, draw
        // phase, screen space); both re-derive from the same collider, so they cannot diverge.
        proxy.Get<TransformComponent>().Position = ProxyGeometry.Centroid(outline);
    }

    private Entity CreateProxyEntity(Entity anchor, ProxyBindingKind kind, int index)
    {
        // Standalone (never ChildOf-parented — DisposeOrphans is live in Edit). The visual is a
        // screen-baked mesh on the native-resolution Editor target; NO VisibleComponent per the
        // chrome rule (the Editor pass renders every matching entity, and its presence would pull
        // the mesh into MeshPrepSystem, which overwrites the identity WorldMatrix the
        // screen-baked vertices require).
        var proxy = _world.CreateEntity();
        proxy.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        proxy.Set(new GizmoProxyComponent(anchor, kind, index));
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
        foreach (var proxy in _proxies.Values)
            if (proxy.IsAlive)
                proxy.Dispose();
        _proxies.Clear();
        _anchor = default;
    }

    public void Dispose()
    {
        DespawnAll();
        _selectedSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
