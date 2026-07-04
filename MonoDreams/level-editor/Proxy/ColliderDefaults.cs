#nullable enable
using System;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Draw;

namespace MonoDreams.LevelEditor.Proxy;

/// <summary>
/// The pure default shapes behind the editor's <b>Add collider</b> actions (island-authoring
/// plan §5.1): a prop/building footprint is the box a top-down character collides with — the
/// sprite's <b>full rendered width × its bottom quarter, anchored at the feet</b> — because in
/// the reference games only the base of a tree/building blocks you (the canopy/roof is walked
/// behind, handled by Y-sorting, not collision).
///
/// <para><b>The math, under the feet-origin convention.</b> <c>BoxColliderComponent.Bounds</c>
/// is Transform-relative (<c>CollisionRect.FromBounds(bounds, transform.Position)</c> — never
/// scaled by the transform), and the sprite's local quad relative to <c>Position</c> spans
/// <c>(-Origin·s) .. (-Origin·s + Size)</c> where <c>s = Size/Source</c> is the source→render
/// scale. The footprint keeps the quad's full width and its bottom
/// <see cref="FootprintHeightFraction"/>: <c>left = -Origin.X·sx</c>,
/// <c>top = -Origin.Y·sy + 0.75·Size.Y</c>, <c>w = Size.X</c>, <c>h = 0.25·Size.Y</c>. On a
/// Y-sorted band the factory's feet-origin (<c>Origin = (srcW/2, srcH)</c>) reduces this to
/// <c>(-w/2, -h) .. (w/2, 0)</c> — the box hangs off the feet point, which IS the entity's
/// <c>Position</c>. A sprite-less entity gets <see cref="FallbackFootprint"/>.</para>
/// </summary>
public static class ColliderDefaults
{
    /// <summary>The footprint's share of the sprite's rendered height (the bottom ~25% — the
    /// plan §5.1 top-down convention; the designer adjusts with the resize handles).</summary>
    public const float FootprintHeightFraction = 0.25f;

    /// <summary>Editor-added footprints are <b>passive</b> (static world geometry) by default —
    /// the <c>WallEntityFactory</c> idiom, verified in island-authoring Slice 3. In this engine
    /// <c>Passive = true</c> means "does not initiate a collision": the footprint is never the
    /// resolver's moved body, so a static prop/building <b>blocks the active player without
    /// drifting</b> when walked into. A <c>Passive = false</c> footprint initiates collisions and
    /// is displaced by resolution — the building would slide away from the player. (Whether a
    /// passive collider reads as a physical blocker or a fire-only trigger is the game's
    /// <c>EntityInfoComponent</c> classification, not this flag — see
    /// <c>ColliderComponentCommand.AddBox</c>.)</summary>
    public const bool FootprintPassive = true;

    /// <summary>The footprint for an entity with no usable sprite size: a small feet-anchored
    /// box (32 wide × 8 tall, bottom edge at the position).</summary>
    public static readonly Rectangle FallbackFootprint = new(-16, -8, 32, 8);

    /// <summary>The default box-collider footprint for <paramref name="sprite"/> (see the class
    /// doc for the math). Falls back to <see cref="FallbackFootprint"/> when the sprite has no
    /// usable size.</summary>
    public static Rectangle FootprintBounds(in SpriteInfoComponent sprite)
    {
        var size = sprite.Size;
        if (size.X <= 0f || size.Y <= 0f)
        {
            if (sprite.Source.Width > 0 && sprite.Source.Height > 0)
                size = new Vector2(sprite.Source.Width, sprite.Source.Height);
            else
                return FallbackFootprint;
        }

        // Origin is in SOURCE pixels; scale it into rendered units like the draw path does.
        var sx = sprite.Source.Width > 0 ? size.X / sprite.Source.Width : 1f;
        var sy = sprite.Source.Height > 0 ? size.Y / sprite.Source.Height : 1f;

        var left = -sprite.Origin.X * sx;
        var top = -sprite.Origin.Y * sy + size.Y * (1f - FootprintHeightFraction);
        var width = MathF.Max(1f, size.X);
        var height = MathF.Max(1f, size.Y * FootprintHeightFraction);
        return new Rectangle(
            (int)MathF.Round(left), (int)MathF.Round(top),
            (int)MathF.Round(width), (int)MathF.Round(height));
    }

    /// <summary>The default polygon footprint: a hexagon inscribed in
    /// <see cref="FootprintBounds"/> — a sensible irregular-base starting shape the designer
    /// then vertex-edits.</summary>
    public static Vector2[] FootprintHexagon(in SpriteInfoComponent sprite)
        => Hexagon(FootprintBounds(sprite));

    /// <summary>The polygon footprint for a sprite-less entity (hexagon inscribed in
    /// <see cref="FallbackFootprint"/>).</summary>
    public static Vector2[] FallbackHexagon() => Hexagon(FallbackFootprint);

    /// <summary>A hexagon inscribed in <paramref name="rect"/>, wound clockwise in the y-down
    /// world (the convex collider's documented winding).</summary>
    public static Vector2[] Hexagon(Rectangle rect)
    {
        float l = rect.Left, t = rect.Top, w = rect.Width, h = rect.Height;
        return new[]
        {
            new Vector2(l + w * 0.25f, t),
            new Vector2(l + w * 0.75f, t),
            new Vector2(l + w, t + h / 2f),
            new Vector2(l + w * 0.75f, t + h),
            new Vector2(l + w * 0.25f, t + h),
            new Vector2(l, t + h / 2f),
        };
    }
}
