#nullable enable
using System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>
/// The pure, allocation-light transform math behind the gizmo (Wave 4b) — separated from
/// <c>GizmoSystem</c> so it is unit-testable without a world, a cursor or a GraphicsDevice. Given a
/// tool, the drag-start transform, the entity's world pivot, and the drag-start + current cursor
/// world points, it returns the new (position, rotation, scale, origin), applying grid-snap when a
/// positive step is supplied.
///
/// <para><b>Snap semantics</b> (per the contract: "quantize the world-space result honoring
/// Origin"): with snap off (step ≤ 0) the raw drag delta is applied; with snap on the world-space
/// result is quantized — translate snaps the resulting position to the grid, rotate snaps the angle
/// to the rotation step, scale snaps the resulting scale to whole steps. Rotate and scale pivot
/// about the entity's world pivot (the world location of its <c>Origin</c>), and the local
/// <c>Origin</c> field is preserved unchanged through every edit.</para>
/// </summary>
public static class GizmoTransform
{
    /// <summary>A drag of this many world units along X doubles the scale (uniform mapping for Wave A).</summary>
    public const float ScaleDragUnit = 64f;

    /// <summary>The minimum scale factor a scale drag can reach (kept above zero so a sprite never inverts/vanishes).</summary>
    public const float MinScaleFactor = 0.05f;

    /// <summary>
    /// Computes the post-drag transform for <paramref name="tool"/> from the before-state plus the
    /// drag's cursor motion. The <c>before*</c> values are LOCAL transform fields (what
    /// <c>TransformComponent</c> stores); <paramref name="worldPivot"/> is the entity's current world
    /// position (the rotate/scale centre); <paramref name="startCursorWorld"/> /
    /// <paramref name="currentCursorWorld"/> are the world-space cursor points at drag-start and now.
    /// <paramref name="snapStep"/> &gt; 0 quantizes the translate position and the scale; with snap on,
    /// <paramref name="rotationSnapStep"/> &gt; 0 quantizes the rotate result. The <c>origin</c> is
    /// returned unchanged.
    /// </summary>
    public static (Vector2 position, float rotation, Vector2 scale, Vector2 origin) Compute(
        GizmoTool tool,
        Vector2 beforePosition, float beforeRotation, Vector2 beforeScale, Vector2 beforeOrigin,
        Vector2 worldPivot, Vector2 startCursorWorld, Vector2 currentCursorWorld,
        float snapStep, float rotationSnapStep)
    {
        switch (tool)
        {
            case GizmoTool.Move:
            {
                var delta = currentCursorWorld - startCursorWorld;
                var target = beforePosition + delta;
                if (snapStep > 0f) target = Snap(target, snapStep);
                return (target, beforeRotation, beforeScale, beforeOrigin);
            }
            case GizmoTool.Rotate:
            {
                var delta = RotationDelta(worldPivot, startCursorWorld, currentCursorWorld);
                var target = beforeRotation + delta;
                if (rotationSnapStep > 0f) target = Snap(target, rotationSnapStep);
                return (beforePosition, target, beforeScale, beforeOrigin);
            }
            case GizmoTool.Scale:
            {
                var delta = currentCursorWorld - startCursorWorld;
                var target = ScaleResult(beforeScale, delta, snapStep);
                return (beforePosition, beforeRotation, target, beforeOrigin);
            }
            default:
                return (beforePosition, beforeRotation, beforeScale, beforeOrigin);
        }
    }

    /// <summary>Quantizes each axis of <paramref name="v"/> to the nearest multiple of
    /// <paramref name="step"/> (which must be positive).</summary>
    public static Vector2 Snap(Vector2 v, float step)
        => new(MathF.Round(v.X / step) * step, MathF.Round(v.Y / step) * step);

    /// <summary>Quantizes <paramref name="value"/> to the nearest multiple of <paramref name="step"/>.</summary>
    public static float Snap(float value, float step)
        => MathF.Round(value / step) * step;

    /// <summary>
    /// The signed rotation (radians) from the start cursor ray to the current cursor ray about the
    /// pivot. A degenerate (zero-length) ray yields zero rotation. The result is wrapped to (−π, π].
    /// </summary>
    public static float RotationDelta(Vector2 worldPivot, Vector2 startCursorWorld, Vector2 currentCursorWorld)
    {
        var a = startCursorWorld - worldPivot;
        var b = currentCursorWorld - worldPivot;
        if (a.LengthSquared() < 1e-6f || b.LengthSquared() < 1e-6f) return 0f;
        return WrapAngle(MathF.Atan2(b.Y, b.X) - MathF.Atan2(a.Y, a.X));
    }

    /// <summary>
    /// The scaled result for a scale drag: the drag's X distance maps to a uniform factor
    /// (<c>1 + dx / <see cref="ScaleDragUnit"/></c>, clamped at <see cref="MinScaleFactor"/>). With
    /// <paramref name="snapStep"/> &gt; 0 the resulting scale is snapped to whole steps of
    /// <paramref name="snapStep"/> on each axis (so e.g. a step of 1 snaps to integer scales).
    /// </summary>
    public static Vector2 ScaleResult(Vector2 beforeScale, Vector2 totalCursorDelta, float snapStep)
    {
        var target = beforeScale * ScaleFactor(totalCursorDelta);
        if (snapStep > 0f) target = Snap(target, snapStep);
        return target;
    }

    /// <summary>
    /// The uniform scale factor a scale drag of <paramref name="totalCursorDelta"/> produces:
    /// <c>1 + dx / <see cref="ScaleDragUnit"/></c>, floored at <see cref="MinScaleFactor"/> (never zero
    /// or negative). Exposed so the camera-rig's Scale-tool drag can map the SAME drag gesture to a zoom
    /// edit (a bigger frustum ⇒ a LOWER zoom: <c>newZoom = beforeZoom / factor</c>) without duplicating
    /// the drag→factor mapping.
    /// </summary>
    public static float ScaleFactor(Vector2 totalCursorDelta)
    {
        var factor = 1f + totalCursorDelta.X / ScaleDragUnit;
        return factor < MinScaleFactor ? MinScaleFactor : factor;
    }

    /// <summary>Wraps an angle to (−π, π].</summary>
    public static float WrapAngle(float radians)
    {
        radians %= MathF.Tau;
        if (radians <= -MathF.PI) radians += MathF.Tau;
        else if (radians > MathF.PI) radians -= MathF.Tau;
        return radians;
    }

    /// <summary>
    /// The four world-space corners of a sprite's rendered quad — the same frame
    /// <c>SpritePrepSystem</c> + <c>MasterRenderSystem</c> draw it in, used for the selection
    /// outline. Corners walk TL → TR → BR → BL so a closed polygon outline traces the border.
    /// </summary>
    public static Vector2[] SpriteWorldQuad(TransformComponent transform, SpriteInfoComponent sprite)
    {
        var localWidth = sprite.Source.Width > 0 ? sprite.Source.Width : sprite.Size.X;
        var localHeight = sprite.Source.Height > 0 ? sprite.Source.Height : sprite.Size.Y;

        var worldScale = transform.WorldScale;
        var destScaleX = sprite.Source.Width > 0 ? sprite.Size.X / sprite.Source.Width : 1f;
        var destScaleY = sprite.Source.Height > 0 ? sprite.Size.Y / sprite.Source.Height : 1f;
        var scaleX = worldScale.X * destScaleX;
        var scaleY = worldScale.Y * destScaleY;

        var drawPosition = transform.WorldPosition + sprite.Offset;
        var rotation = transform.WorldRotation;
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);

        Vector2 Local(float lx, float ly)
        {
            var sx = (lx - sprite.Origin.X) * scaleX;
            var sy = (ly - sprite.Origin.Y) * scaleY;
            return drawPosition + new Vector2(sx * cos - sy * sin, sx * sin + sy * cos);
        }

        return new[]
        {
            Local(0f, 0f),
            Local(localWidth, 0f),
            Local(localWidth, localHeight),
            Local(0f, localHeight),
        };
    }
}
