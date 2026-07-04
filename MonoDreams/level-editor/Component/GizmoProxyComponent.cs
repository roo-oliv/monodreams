using DefaultEcs;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Which spatial field of the bound entity a gizmo proxy edits. This is the <b>generalization
/// seam</b> for component-local spatial data: colliders are NOT entities — their shapes live as
/// fields on the game entity's components (<c>BoxColliderComponent.Bounds</c>,
/// <c>ConvexColliderComponent.ModelVertices</c>) — so the editor cannot grab them with the
/// transform gizmo directly. A proxy binds a standalone handle entity to
/// <c>(entity, component, field)</c> through this kind; adding a new editable spatial field
/// (e.g. the road tool's spline control points, Waves D/F) is a new enum member + a derivation
/// case in <c>Proxy/ProxyGeometry</c> + a write-back case in the gizmo's proxy drag — never a
/// new proxy mechanism.
/// </summary>
public enum ProxyBindingKind
{
    /// <summary>Edits <c>BoxColliderComponent.Bounds</c> (the entity-relative AABB): a drag
    /// shifts the rectangle's X/Y by the world delta.</summary>
    BoxColliderBounds,

    /// <summary>Edits <c>ConvexColliderComponent.ModelVertices</c> as a whole shape: a drag
    /// translates every local-space vertex by the (inverse-transformed) world delta.</summary>
    ConvexColliderShape,

    /// <summary>Edits ONE entry of <c>ConvexColliderComponent.ModelVertices</c> —
    /// <see cref="GizmoProxyComponent.Index"/> carries the vertex ordinal. A drag moves that
    /// vertex by the (inverse-transformed) world delta; a result that would make the polygon
    /// non-convex is rejected (not applied) — the loud-reject convexity strategy (see the
    /// vertex-editing premise). Vertex proxies materialize while the convex family's own proxy
    /// (shape or vertex) is selected, so the handles appear one click deep instead of cluttering
    /// every selection.</summary>
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
/// The pure-data binding of an edit-time gizmo proxy (Wave 8b): a standalone handle entity the
/// editor materializes over component-local spatial data of <see cref="Target"/>, so that data
/// becomes selectable and draggable through the ordinary selection + gizmo path. The proxy entity
/// carries this component plus <c>TransformComponent</c> (kept at the bound shape's world centre
/// by <c>ProxySyncSystem</c>, so the gizmo's pivot/handles work unchanged), a mesh
/// <c>DrawComponent</c> (the distinct outline visual, world-space on Main) and a self-set
/// <c>VisibleComponent</c>.
///
/// <para><b>Standalone, transient, never written back into.</b> Like every editor overlay entity
/// it is never <c>ChildOfComponent</c>-parented (<c>HierarchySystem.DisposeOrphans</c> is live in
/// Edit) and is absent from the serializer registry (despawned on deselect / mode exit / target
/// death). Crucially, dragging a proxy does <b>not</b> persist the proxy's own transform — the
/// gizmo routes the drag into a <c>ColliderEditCommand</c> against <see cref="Target"/>'s bound
/// component field, through the coalescing undo transaction. An undo command recorded against the
/// transient proxy would dangle the moment the proxy despawns; the bound component on the game
/// entity is the durable thing.</para>
/// </summary>
public struct GizmoProxyComponent
{
    /// <summary>The game entity whose component field this proxy edits.</summary>
    public Entity Target;

    /// <summary>Which spatial field of <see cref="Target"/> the proxy is bound to.</summary>
    public ProxyBindingKind Kind;

    /// <summary>
    /// Sub-element index for bindings that address one element of a collection — a
    /// <see cref="ProxyBindingKind.ConvexVertex"/> handle (or a future spline control point,
    /// Waves D/F) carries the element's ordinal here. Whole-shape bindings use 0; the proxy
    /// family is keyed <c>(Kind, Index)</c> (see <c>ProxySyncSystem</c>).
    /// </summary>
    public int Index;

    public GizmoProxyComponent(Entity target, ProxyBindingKind kind, int index = 0)
    {
        Target = target;
        Kind = kind;
        Index = index;
    }
}
