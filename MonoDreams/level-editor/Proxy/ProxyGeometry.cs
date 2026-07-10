#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Extensions.Monogame;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Proxy;

/// <summary>
/// The pure, allocation-light geometry behind the collider gizmo proxies (Wave 8b) — separated
/// from <c>ProxySyncSystem</c> / <c>SelectionSystem</c> / <c>GizmoSystem</c> so the derivations
/// are unit-testable and there is exactly <b>one</b> source of truth for where a bound shape sits
/// in the world: the sync (proxy placement + outline mesh), the selection (border hit-test) and
/// the gizmo (drag reference point) all call these.
///
/// <para><b>The derivations deliberately mirror the collision module.</b> Box corners are
/// <c>Transform.WorldPosition + Bounds</c> offsets, axis-aligned, exactly as
/// <c>ColliderDebugSystem</c> draws them; convex world vertices reproduce
/// <c>ConvexColliderComponent.UpdateWorldVertices</c> (scale → rotate → translate by the WORLD transform
/// — <c>WorldPosition</c>/<c>WorldRotation</c>/<c>WorldScale</c>, honoring <c>IgnoreTransformRotation</c>) without mutating the collider — so
/// the proxy shows the exact truth the collision system will use, for a child entity (a prefab
/// instance's child collider) as much as a root.</para>
///
/// <para><b>Extension point (Waves D/F).</b> A new <see cref="ProxyBindingKind"/> (e.g. a spline
/// control point) adds its derivation case to <see cref="TryGetWorldOutline"/> here; everything
/// downstream (sync placement, border pick, gizmo pivot) follows without new mechanism.</para>
/// </summary>
public static class ProxyGeometry
{
    /// <summary>The world-unit half-extent of a <see cref="ProxyBindingKind.ConvexVertex"/>
    /// handle's pick outline (a small square around the vertex). Deliberately small: the border
    /// pick's <c>8px/Zoom</c> tolerance is what gives the handle its comfortable grab radius —
    /// this square only anchors the border test (and the centroid) at the vertex itself.</summary>
    public const float VertexHandleWorldHalfExtent = 2f;

    /// <summary>
    /// The world-space outline polygon of the sub-element <paramref name="kind"/> binds on
    /// <paramref name="target"/> — a small square around the <paramref name="index"/>-th convex
    /// vertex / boundary point, or the boundary's thickness handle. False when the target is dead,
    /// no longer carries the bound component, or the index is out of range (a stale vertex proxy
    /// after a delete). (The former whole-shape box/convex cases are retired — a collider is its
    /// own entity now; see <see cref="TryGetColliderWorldShape"/> for a collider entity's outline.)
    /// </summary>
    public static bool TryGetWorldOutline(Entity target, ProxyBindingKind kind, int index, out Vector2[] outline)
    {
        outline = Array.Empty<Vector2>();
        if (!target.IsAlive || !target.Has<TransformComponent>()) return false;
        var transform = target.Get<TransformComponent>();

        switch (kind)
        {
            case ProxyBindingKind.ConvexVertex:
                if (!target.Has<ConvexColliderComponent>()) return false;
                var collider = target.Get<ConvexColliderComponent>();
                if (collider.ModelVertices == null || index < 0 || index >= collider.ModelVertices.Length)
                    return false;
                outline = VertexHandleSquare(ConvexVertexWorld(transform, collider, index));
                return true;

            case ProxyBindingKind.BoundaryVertex:
                if (!target.Has<BoundaryComponent>()) return false;
                var boundary = target.Get<BoundaryComponent>();
                if (boundary.Points == null || index < 0 || index >= boundary.Points.Length)
                    return false;
                // A boundary's Points are LOCAL to its Position (no rotation/scale), so the world
                // vertex is Position + the local point.
                outline = VertexHandleSquare(transform.Position + boundary.Points[index]);
                return true;

            case ProxyBindingKind.BoundaryThickness:
                if (!target.Has<BoundaryComponent>()) return false;
                var band = target.Get<BoundaryComponent>();
                if (band.Points == null || band.Points.Length < BoundaryGeometry.MinPoints) return false;
                // The thickness handle rides the band edge: first-edge midpoint + normal × t/2, local
                // to Position (no rotation/scale).
                outline = VertexHandleSquare(
                    transform.Position + BoundaryGeometry.ThicknessHandleLocal(band.Points, band.Thickness));
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// The world-space outline polygon of a collider ENTITY's shape — box corners (TL→TR→BR→BL)
    /// or the convex world vertices — derived from the entity's own <c>TransformComponent</c>
    /// (colliders-as-entities). This is the pick surface + selection outline for a spriteless
    /// collider entity (the camera-rig border-pick precedent): the collider is selected by a
    /// border click on this outline and moved/scaled by the ordinary gizmo. False when the entity
    /// is dead, has no transform, or carries no (usable) shape component.
    /// </summary>
    public static bool TryGetColliderWorldShape(Entity entity, out Vector2[] outline)
    {
        outline = Array.Empty<Vector2>();
        if (!entity.IsAlive || !entity.Has<TransformComponent>()) return false;
        var transform = entity.Get<TransformComponent>();

        if (entity.Has<BoxColliderComponent>())
        {
            outline = BoxWorldCorners(transform, entity.Get<BoxColliderComponent>());
            return true;
        }
        if (entity.Has<ConvexColliderComponent>())
        {
            var convex = entity.Get<ConvexColliderComponent>();
            if (convex.ModelVertices == null || convex.ModelVertices.Length < 3) return false;
            outline = ConvexWorldVertices(transform, convex);
            return true;
        }
        return false;
    }

    /// <summary>A small world-space square around <paramref name="world"/> — the pick anchor a
    /// per-vertex handle (<see cref="ProxyBindingKind.ConvexVertex"/> /
    /// <see cref="ProxyBindingKind.BoundaryVertex"/>) hit-tests against; the visible handle size is
    /// the constant-on-screen square the sync/gizmo draw.</summary>
    public static Vector2[] VertexHandleSquare(Vector2 world)
    {
        var h = VertexHandleWorldHalfExtent;
        return new[]
        {
            world + new Vector2(-h, -h), world + new Vector2(h, -h),
            world + new Vector2(h, h), world + new Vector2(-h, h),
        };
    }

    /// <summary>Whether <paramref name="kind"/> is a point handle drawn as a constant-on-screen
    /// square rather than a full outline — the per-vertex handles and the boundary thickness
    /// handle.</summary>
    public static bool IsVertexHandle(ProxyBindingKind kind) =>
        kind is ProxyBindingKind.ConvexVertex or ProxyBindingKind.BoundaryVertex
            or ProxyBindingKind.BoundaryThickness;

    /// <summary>The box collider's four world corners (TL→TR→BR→BL) — the axis-aligned rect
    /// <b>centered</b> on the collider entity's <c>WorldPosition</c> with extent <c>Size</c> (scaled),
    /// exactly the quad detection tests and <c>ColliderDebugSystem</c> outlines
    /// (<c>SATCollision.BoxWorldRect</c> is the single source of the box pose).</summary>
    public static Vector2[] BoxWorldCorners(TransformComponent transform, BoxColliderComponent box)
    {
        var rect = SATCollision.BoxWorldRect(box, transform);
        return new[]
        {
            new Vector2(rect.Left, rect.Top),
            new Vector2(rect.Right, rect.Top),
            new Vector2(rect.Right, rect.Bottom),
            new Vector2(rect.Left, rect.Bottom),
        };
    }

    /// <summary>
    /// The convex collider's world vertices, computed purely (never mutating the collider's own
    /// <c>WorldVertices</c>): scale, then rotate (unless <c>IgnoreTransformRotation</c>), then
    /// translate by the WORLD transform — the same math as
    /// <c>ConvexColliderComponent.UpdateWorldVertices</c>, correct for a child entity as much as a root.
    /// </summary>
    public static Vector2[] ConvexWorldVertices(TransformComponent transform, ConvexColliderComponent collider)
    {
        var pos = transform.WorldPosition;
        var rot = collider.IgnoreTransformRotation ? 0f : transform.WorldRotation;
        var scale = transform.WorldScale;
        var cos = MathF.Cos(rot);
        var sin = MathF.Sin(rot);

        var result = new Vector2[collider.ModelVertices.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var v = collider.ModelVertices[i];
            var sx = v.X * scale.X;
            var sy = v.Y * scale.Y;
            result[i] = new Vector2(
                sx * cos - sy * sin + pos.X,
                sx * sin + sy * cos + pos.Y);
        }

        return result;
    }

    /// <summary>One convex model vertex mapped to world space — the same scale → rotate →
    /// translate-by-WORLD-transform math as <see cref="ConvexWorldVertices"/>, for a single
    /// <paramref name="index"/> (a <see cref="ProxyBindingKind.ConvexVertex"/> handle's anchor).</summary>
    public static Vector2 ConvexVertexWorld(TransformComponent transform, ConvexColliderComponent collider, int index)
    {
        var pos = transform.WorldPosition;
        var rot = collider.IgnoreTransformRotation ? 0f : transform.WorldRotation;
        var scale = transform.WorldScale;
        var cos = MathF.Cos(rot);
        var sin = MathF.Sin(rot);
        var v = collider.ModelVertices[index];
        var sx = v.X * scale.X;
        var sy = v.Y * scale.Y;
        return new Vector2(sx * cos - sy * sin + pos.X, sx * sin + sy * cos + pos.Y);
    }

    /// <summary>
    /// Whether an ordered vertex loop describes a convex polygon: every consecutive edge pair
    /// turns the same way (all non-zero cross products share one sign). Collinear triples
    /// (zero cross) are allowed — a just-inserted edge-midpoint vertex is collinear by
    /// construction and must be legal — but a fully degenerate loop (all collinear, zero area)
    /// is not a polygon and returns false. This is the vertex-editing loud-reject guard: a drag
    /// frame whose result fails this check is not applied.
    /// </summary>
    public static bool IsConvex(Vector2[] vertices)
    {
        if (vertices == null || vertices.Length < 3) return false;
        var sign = 0f;
        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Length];
            var c = vertices[(i + 2) % vertices.Length];
            var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (MathF.Abs(cross) < 1e-6f) continue; // collinear (e.g. a fresh midpoint vertex)
            if (sign == 0f) sign = MathF.Sign(cross);
            else if (MathF.Sign(cross) != sign) return false;
        }
        return sign != 0f; // an all-collinear loop has no interior
    }

    /// <summary>The arithmetic centroid of a point set — the proxy's pivot (where the gizmo's
    /// move handle sits) and the rotate/scale-free reference for the drag delta.</summary>
    public static Vector2 Centroid(Vector2[] points)
    {
        if (points == null || points.Length == 0) return Vector2.Zero;
        var sum = Vector2.Zero;
        foreach (var p in points) sum += p;
        return sum / points.Length;
    }

    /// <summary>
    /// True when <paramref name="point"/> lies within <paramref name="tolerance"/> of the closed
    /// polygon's <b>border</b> (any edge segment). The proxy deliberately hit-tests only its
    /// outline — never its fill — so a collider that fully covers its entity's sprite (the common
    /// tile shape) does not shadow the sprite pick: clicking the drawn outline grabs the proxy,
    /// clicking inside still picks the entity.
    /// </summary>
    public static bool BorderContains(Vector2[] polygon, Vector2 point, float tolerance)
    {
        if (polygon == null || polygon.Length < 2 || tolerance <= 0f) return false;
        var tolSq = tolerance * tolerance;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            if (DistanceSquaredToSegment(point, a, b) <= tolSq) return true;
        }
        return false;
    }

    /// <summary>Squared distance from <paramref name="point"/> to the segment
    /// <paramref name="a"/>–<paramref name="b"/> (degenerate segments collapse to the point test).</summary>
    public static float DistanceSquaredToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSq = ab.LengthSquared();
        if (lengthSq < 1e-12f) return Vector2.DistanceSquared(point, a);
        var t = Vector2.Dot(point - a, ab) / lengthSq;
        t = MathHelper.Clamp(t, 0f, 1f);
        return Vector2.DistanceSquared(point, a + ab * t);
    }

    /// <summary>
    /// Maps a world-space drag delta into the collider's model space — the inverse of the
    /// scale-then-rotate the world-vertex derivation applies — so translating every
    /// <c>ModelVertices</c> entry by the result moves the shape's <b>world</b> outline by exactly
    /// <paramref name="worldDelta"/>, whatever the entity's rotation/scale.
    /// <paramref name="ignoreRotation"/> mirrors <c>ConvexColliderComponent.IgnoreTransformRotation</c>.
    /// A zero scale axis yields zero delta on that axis (nothing sensible to invert).
    /// </summary>
    public static Vector2 WorldDeltaToModelDelta(TransformComponent transform, bool ignoreRotation, Vector2 worldDelta)
    {
        var rot = ignoreRotation ? 0f : transform.Rotation;
        var cos = MathF.Cos(-rot);
        var sin = MathF.Sin(-rot);
        var unrotated = new Vector2(
            worldDelta.X * cos - worldDelta.Y * sin,
            worldDelta.X * sin + worldDelta.Y * cos);

        var scale = transform.Scale;
        return new Vector2(
            scale.X != 0f ? unrotated.X / scale.X : 0f,
            scale.Y != 0f ? unrotated.Y / scale.Y : 0f);
    }
}
