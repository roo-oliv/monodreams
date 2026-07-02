#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
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
/// <c>ConvexColliderComponent.UpdateWorldVertices</c> (scale → rotate → translate by the LOCAL
/// <c>Position</c>, honoring <c>IgnoreTransformRotation</c>) without mutating the collider — so
/// the proxy shows the truth the collision system will use, including its documented
/// root-level-entities-only limitation.</para>
///
/// <para><b>Extension point (Waves D/F).</b> A new <see cref="ProxyBindingKind"/> (e.g. a spline
/// control point) adds its derivation case to <see cref="TryGetWorldOutline"/> here; everything
/// downstream (sync placement, border pick, gizmo pivot) follows without new mechanism.</para>
/// </summary>
public static class ProxyGeometry
{
    /// <summary>
    /// The world-space outline polygon of the shape <paramref name="kind"/> binds on
    /// <paramref name="target"/> — box corners (TL→TR→BR→BL) or the convex world vertices.
    /// False when the target is dead or no longer carries the bound component.
    /// </summary>
    public static bool TryGetWorldOutline(Entity target, ProxyBindingKind kind, out Vector2[] outline)
    {
        outline = Array.Empty<Vector2>();
        if (!target.IsAlive || !target.Has<TransformComponent>()) return false;
        var transform = target.Get<TransformComponent>();

        switch (kind)
        {
            case ProxyBindingKind.BoxColliderBounds:
                if (!target.Has<BoxColliderComponent>()) return false;
                outline = BoxWorldCorners(transform, target.Get<BoxColliderComponent>());
                return true;

            case ProxyBindingKind.ConvexColliderShape:
                if (!target.Has<ConvexColliderComponent>()) return false;
                var convex = target.Get<ConvexColliderComponent>();
                if (convex.ModelVertices == null || convex.ModelVertices.Length < 3) return false;
                outline = ConvexWorldVertices(transform, convex);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The box collider's four world corners (TL→TR→BR→BL) — the axis-aligned AABB at
    /// <c>WorldPosition + Bounds</c>, exactly the quad <c>ColliderDebugSystem</c> outlines.</summary>
    public static Vector2[] BoxWorldCorners(TransformComponent transform, BoxColliderComponent box)
    {
        var wp = transform.WorldPosition;
        return new[]
        {
            new Vector2(wp.X + box.Bounds.Left, wp.Y + box.Bounds.Top),
            new Vector2(wp.X + box.Bounds.Right, wp.Y + box.Bounds.Top),
            new Vector2(wp.X + box.Bounds.Right, wp.Y + box.Bounds.Bottom),
            new Vector2(wp.X + box.Bounds.Left, wp.Y + box.Bounds.Bottom),
        };
    }

    /// <summary>
    /// The convex collider's world vertices, computed purely (never mutating the collider's own
    /// <c>WorldVertices</c>): scale, then rotate (unless <c>IgnoreTransformRotation</c>), then
    /// translate by the local <c>Position</c> — the same math as
    /// <c>ConvexColliderComponent.UpdateWorldVertices</c>, root-level-entity contract included.
    /// </summary>
    public static Vector2[] ConvexWorldVertices(TransformComponent transform, ConvexColliderComponent collider)
    {
        var pos = transform.Position;
        var rot = collider.IgnoreTransformRotation ? 0f : transform.Rotation;
        var scale = transform.Scale;
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
