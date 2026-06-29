#nullable enable
using System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;

namespace MonoDreams.LevelEditor.Selection;

/// <summary>
/// Pure, allocation-free world-space sprite hit-testing for the editor's selection picking.
/// It reproduces the exact transform <c>SpritePrepSystem</c> + <c>MasterRenderSystem</c> use to
/// draw a sprite (so what the designer clicks is what they see), then asks whether a world point
/// falls inside the sprite's rendered quad — honoring rotation, scale, origin and offset.
///
/// <para><b>The rendered quad.</b> A sprite draws at <c>WorldPosition + Offset</c> with the pivot
/// <c>SpriteInfo.Origin</c> (in source-texture pixels), rotated by <c>WorldRotation</c>, and scaled
/// by <c>WorldScale × (Size / Source)</c> — the destination-size scale (<c>Size/Source</c>, applied
/// by <c>MasterRenderSystem.DrawElement</c>) folded with the transform's world scale (applied by
/// <c>SpritePrepSystem</c>). The quad therefore spans source-space <c>[0, Source.W] × [0, Source.H]</c>
/// about the origin, in that scaled/rotated/translated frame.</para>
///
/// <para>Hit-testing inverts that frame: it maps the world point back into the sprite's unscaled,
/// unrotated local space about the origin and tests against the axis-aligned local rectangle. This
/// is exact for any rotation (no enlarged AABB), which a rotated sprite needs to pick correctly.</para>
/// </summary>
public static class SpriteHitTest
{
    /// <summary>
    /// True if <paramref name="worldPoint"/> falls inside the rendered quad of the sprite described
    /// by <paramref name="transform"/> + <paramref name="sprite"/>. When <c>Source</c> has zero
    /// width/height (no source rect) the sprite's <c>Size</c> is used directly as the local extent.
    /// </summary>
    public static bool Contains(in TransformComponent transform, in SpriteInfoComponent sprite, Vector2 worldPoint)
    {
        // Local extent in source-pixel space (the quad's pre-scale dimensions).
        var localWidth = sprite.Source.Width > 0 ? sprite.Source.Width : sprite.Size.X;
        var localHeight = sprite.Source.Height > 0 ? sprite.Source.Height : sprite.Size.Y;
        if (localWidth <= 0f || localHeight <= 0f) return false;

        // Effective per-axis scale: world scale × (destination size / source size). Mirrors the
        // scale MasterRenderSystem.DrawElement computes (Size/Source) times SpritePrepSystem's WorldScale.
        var worldScale = transform.WorldScale;
        var destScaleX = sprite.Source.Width > 0 ? sprite.Size.X / sprite.Source.Width : 1f;
        var destScaleY = sprite.Source.Height > 0 ? sprite.Size.Y / sprite.Source.Height : 1f;
        var scaleX = worldScale.X * destScaleX;
        var scaleY = worldScale.Y * destScaleY;
        if (scaleX == 0f || scaleY == 0f) return false;

        var drawPosition = transform.WorldPosition + sprite.Offset;
        var rotation = transform.WorldRotation;

        // Translate into the draw frame, then undo rotation (rotate by -rotation about the draw pos).
        var rel = worldPoint - drawPosition;
        var cos = MathF.Cos(-rotation);
        var sin = MathF.Sin(-rotation);
        var localRotated = new Vector2(
            rel.X * cos - rel.Y * sin,
            rel.X * sin + rel.Y * cos);

        // Undo scale → back to source-pixel space, then re-add the origin so the rect is [0,W]×[0,H].
        var localX = localRotated.X / scaleX + sprite.Origin.X;
        var localY = localRotated.Y / scaleY + sprite.Origin.Y;

        return localX >= 0f && localX <= localWidth && localY >= 0f && localY <= localHeight;
    }
}
