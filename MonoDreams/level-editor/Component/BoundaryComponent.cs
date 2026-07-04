#nullable enable
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// A freeform world <b>boundary</b> (island-authoring plan §5.2 — coastline / cliff): an ordered
/// polyline of local-space <see cref="Points"/> plus a <see cref="Thickness"/>. It is <b>pure,
/// serialized scene data</b> (registered in the component-serializer registry → it round-trips in
/// <c>entities[]</c>), the <b>durable truth</b> of the boundary. The physical collision it produces
/// is <b>baked, never per-frame</b>: <c>BoundaryBakeSystem</c> reacts to this component being
/// added/changed and generates one thin convex quad segment collider per polyline edge as
/// <c>ChildOf</c> children of the boundary entity; those bake products carry
/// <see cref="BakedProductComponent"/> and are <b>never serialized</b> (they regenerate on load).
///
/// <para><b>Points are local to the entity's <c>TransformComponent.Position</c></b> (the boundary's
/// pivot = the polyline centroid at commit) — the same local-space convention
/// <c>ConvexColliderComponent.ModelVertices</c> uses, so per-vertex proxies
/// (<see cref="ProxyBindingKind.BoundaryVertex"/>) and the bake share one coordinate frame. A
/// coastline is an <b>open</b> polyline (N points → N−1 edges), not a closed loop.</para>
///
/// <para>ECS purity: no logic here — the bake lives in a system, the vertex editing in the proxy +
/// gizmo path, the durable shape in these two fields.</para>
/// </summary>
public struct BoundaryComponent
{
    /// <summary>The polyline vertices in the entity's LOCAL space (relative to
    /// <c>TransformComponent.Position</c>), in lay order. At least two points describe a boundary.</summary>
    public Vector2[] Points;

    /// <summary>The collision band width in world units: each baked segment quad spans this
    /// thickness centered on its polyline edge. Must be &gt; 0.</summary>
    public float Thickness;

    /// <summary>A sensible default band width for a coastline (world units). The designer widens it
    /// later by dragging the Slice-4 thickness handle.</summary>
    public const float DefaultThickness = 16f;

    /// <summary>The smallest band width the thickness handle allows (world units) — a positive floor
    /// so a drag can never collapse the band to zero (which would bake no quads).</summary>
    public const float MinThickness = 1f;

    public BoundaryComponent(Vector2[] points, float thickness = DefaultThickness)
    {
        Points = points;
        Thickness = thickness;
    }
}
