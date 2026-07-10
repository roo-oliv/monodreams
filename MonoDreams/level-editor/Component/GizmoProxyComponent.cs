using DefaultEcs;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Which spatial field of the bound entity a gizmo proxy edits. This is the <b>generalization
/// seam</b> for <b>sub-element</b> spatial data that is NOT itself an entity — a polygon's
/// individual vertices, a boundary polyline's points, a boundary's thickness. (A collider is now
/// its OWN entity — a shape component + its <c>TransformComponent</c> — so the collider itself is
/// selected + moved + scaled through the ordinary selection/gizmo path, needing no proxy; the CE
/// wave retired the whole-shape <c>BoxColliderBounds</c>/<c>ConvexColliderShape</c> proxies. What
/// remains a proxy is the point-level editing a vertex is too fine to be an entity for.) A proxy
/// binds a standalone handle entity to <c>(entity, kind, index)</c>; adding a new editable
/// sub-element field (e.g. the road tool's spline control points, Waves D/F) is a new enum member
/// + a derivation case in <c>Proxy/ProxyGeometry</c> + a write-back case in the gizmo's proxy drag —
/// never a new proxy mechanism.
/// </summary>
public enum ProxyBindingKind
{
    /// <summary>Edits ONE entry of the collider ENTITY's own
    /// <c>ConvexColliderComponent.ModelVertices</c> — <see cref="GizmoProxyComponent.Index"/>
    /// carries the vertex ordinal. A drag moves that vertex by the (inverse-transformed) world
    /// delta; a result that would make the polygon non-convex is rejected (not applied) — the
    /// loud-reject convexity strategy (see the vertex-editing premise). Vertex proxies materialize
    /// while the convex collider ENTITY (or one of its own vertex proxies) is selected, so the
    /// grips appear on selecting the collider and never clutter an unrelated selection.</summary>
    ConvexVertex,

    /// <summary>Edits ONE entry of <c>BoundaryComponent.Points</c> (island-authoring Slice 3 —
    /// the freeform coastline/cliff polyline); <see cref="GizmoProxyComponent.Index"/> carries the
    /// vertex ordinal. A drag moves that point by the (inverse-transformed) world delta through a
    /// <c>BoundaryEditCommand</c>, which re-fires the boundary bake. Unlike
    /// <see cref="ConvexVertex"/> there is <b>no convexity constraint</b> (a boundary is an open
    /// polyline, not a convex hull); the delete guard keeps at least
    /// <c>BoundaryGeometry.MinPoints</c> vertices. Boundary vertex handles materialize on PLAIN
    /// selection of the boundary entity — a boundary IS its points, so there is no shape proxy to
    /// click through first.</summary>
    BoundaryVertex,

    /// <summary>Edits <c>BoundaryComponent.Thickness</c> — the baked collision band width
    /// (island-authoring Slice 4). A single handle (<see cref="GizmoProxyComponent.Index"/> 0) rides
    /// the edge of the band, at the first edge's midpoint offset by the edge normal × thickness/2;
    /// dragging it along that normal changes the thickness through a <c>BoundaryEditCommand</c>
    /// (one drag = one undo step), which re-fires the boundary bake. Like
    /// <see cref="BoundaryVertex"/> it materializes on PLAIN selection of the boundary.</summary>
    BoundaryThickness,
}

/// <summary>
/// The pure-data binding of an edit-time gizmo proxy: a standalone handle entity the editor
/// materializes over a <b>sub-element</b> of <see cref="Target"/> (a polygon vertex, a boundary
/// point, a boundary's thickness) — data too fine to be its own entity — so that sub-element
/// becomes selectable and draggable through the ordinary selection + gizmo path. The proxy entity
/// carries this component plus <c>TransformComponent</c> (kept at the bound point's world position
/// by <c>ProxySyncSystem</c>, so the gizmo's pivot/handle works unchanged), a mesh
/// <c>DrawComponent</c> (the distinct handle visual) and NO <c>VisibleComponent</c> (the chrome
/// rule).
///
/// <para><b>Standalone, transient, never written back into.</b> Like every editor overlay entity
/// it is never <c>ChildOfComponent</c>-parented (<c>HierarchySystem.DisposeOrphans</c> is live in
/// Edit) and is absent from the serializer registry (despawned on deselect / mode exit / target
/// death). Crucially, dragging a proxy does <b>not</b> persist the proxy's own transform — the
/// gizmo routes the drag into a <c>ColliderEditCommand</c> / <c>BoundaryEditCommand</c> against
/// <see cref="Target"/>'s bound field, through the coalescing undo transaction. An undo command
/// recorded against the transient proxy would dangle the moment the proxy despawns; the bound
/// component on the durable <see cref="Target"/> entity is the thing edited.</para>
/// </summary>
public struct GizmoProxyComponent
{
    /// <summary>The game entity whose component field this proxy edits.</summary>
    public Entity Target;

    /// <summary>Which spatial field of <see cref="Target"/> the proxy is bound to.</summary>
    public ProxyBindingKind Kind;

    /// <summary>
    /// Sub-element index for bindings that address one element of a collection — a
    /// <see cref="ProxyBindingKind.ConvexVertex"/> / <see cref="ProxyBindingKind.BoundaryVertex"/>
    /// handle (or a future spline control point, Waves D/F) carries the element's ordinal here.
    /// The single-handle <see cref="ProxyBindingKind.BoundaryThickness"/> uses 0; the proxy family
    /// is keyed <c>(Kind, Index)</c> (see <c>ProxySyncSystem</c>).
    /// </summary>
    public int Index;

    public GizmoProxyComponent(Entity target, ProxyBindingKind kind, int index = 0)
    {
        Target = target;
        Kind = kind;
        Index = index;
    }
}
