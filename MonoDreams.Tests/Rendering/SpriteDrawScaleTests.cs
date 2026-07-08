using System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "A sprite's drawn quad honors Transform.WorldScale exactly once"
/// (UX2-A bug 1). <c>MasterRenderSystem</c>'s Sprite case used to compute the draw scale as
/// <c>Size / source</c> whenever a source rect existed — every placed prop has one — and DISCARD
/// <c>DrawComponent.Scale</c>, which <c>SpritePrepSystem</c> sets from <c>Transform.WorldScale</c>.
/// So gizmo-scaling grew the selection outline / colliders (transform math) but never the sprite.
/// The fix composes <c>(Size / source) · Scale</c>, so the drawn quad matches the hit-test quad
/// (<c>GizmoTransform.SpriteWorldQuad</c>) that the selection outline and picking already use.
/// </summary>
public class SpriteDrawScaleTests
{
    // Rebuild the four world-space corners the way SpriteBatch.Draw rasterizes a sprite: each
    // source-space corner (lx,ly), minus the origin (in source pixels), times the draw scale,
    // rotated, plus the draw position. Corners walk TL→TR→BR→BL to line up with SpriteWorldQuad.
    private static Vector2[] DrawnQuad(DrawComponent dc)
    {
        var scale = MasterRenderSystem.ComputeSpriteScale(dc);
        var src = dc.SourceRectangle!.Value;
        var cos = MathF.Cos(dc.Rotation);
        var sin = MathF.Sin(dc.Rotation);

        Vector2 Corner(float lx, float ly)
        {
            var sx = (lx - dc.Origin.X) * scale.X;
            var sy = (ly - dc.Origin.Y) * scale.Y;
            return dc.Position + new Vector2(sx * cos - sy * sin, sx * sin + sy * cos);
        }

        return new[]
        {
            Corner(0f, 0f),
            Corner(src.Width, 0f),
            Corner(src.Width, src.Height),
            Corner(0f, src.Height),
        };
    }

    // Exactly what SpritePrepSystem writes into a regular sprite's DrawComponent each frame (the
    // audited path: Size = source size, Scale = WorldScale — WorldScale is NOT pre-baked into Size).
    private static DrawComponent PreppedDraw(TransformComponent t, SpriteInfoComponent s) => new()
    {
        Type = DrawElementType.Sprite,
        Position = t.WorldPosition + s.Offset,
        Rotation = t.WorldRotation,
        Scale = t.WorldScale,
        Size = s.Size,
        SourceRectangle = s.Source,
        Origin = s.Origin,
        Color = s.Color,
    };

    [Fact]
    public void WorldScaledSprite_DrawnQuad_MatchesHitTestQuad()
    {
        // A placed prop: 48×32 source, Size == source dims (SpritePropFactory sets Size = source),
        // feet origin (Y-sorted band), then world-scaled 2×3 by a gizmo scale drag and rotated.
        var source = new Rectangle(0, 0, 48, 32);
        var sprite = new SpriteInfoComponent
        {
            Source = source,
            Size = new Vector2(source.Width, source.Height),
            Origin = SpritePropFactory.FeetOrigin(source), // (24, 32)
            Target = RenderTargetID.Main,
        };
        var transform = new TransformComponent(
            new Vector2(120f, 80f), rotation: 0.4f, scale: new Vector2(2f, 3f));

        var drawn = DrawnQuad(PreppedDraw(transform, sprite));
        var hit = GizmoTransform.SpriteWorldQuad(transform, sprite);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(hit[i].X, drawn[i].X, 3);
            Assert.Equal(hit[i].Y, drawn[i].Y, 3);
        }
    }

    [Fact]
    public void UnitScaleSourceRectSprite_ComputeScale_IsByteIdenticalToOldSizeOverSource()
    {
        // The pre-fix behavior for a source-rect sprite was scale = Size / source. With Scale == One
        // (every existing unscaled sprite: reloaded props, the reference levels), the composition
        // must reproduce it exactly — no visual regression.
        var dc = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            SourceRectangle = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(48f, 48f),
            Scale = Vector2.One,
        };

        var scale = MasterRenderSystem.ComputeSpriteScale(dc);
        Assert.Equal(3f, scale.X); // 48/16, exactly (byte-identical to the old Size/source)
        Assert.Equal(3f, scale.Y);
    }

    [Fact]
    public void DeliberateSizeWithUnitScale_DoesNotDoubleScale_ThumbnailAndCursorPath()
    {
        // The palette thumbnail (Editor target, bypasses SpritePrepSystem) and the textured cursor
        // set DrawComponent.Size deliberately and leave Scale at its default (1,1). The composition
        // must therefore NOT double-scale them — it must equal the plain Size/source fit.
        var dc = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            SourceRectangle = new Rectangle(0, 0, 100, 50),
            Size = new Vector2(40f, 20f), // the fitted destination box the thumbnail computes
            // Scale left at its default — the invariant these paths rely on.
        };

        Assert.Equal(Vector2.One, dc.Scale);
        var scale = MasterRenderSystem.ComputeSpriteScale(dc);
        Assert.Equal(0.4f, scale.X, 5); // 40/100 — the fit only, not fit × something
        Assert.Equal(0.4f, scale.Y, 5); // 20/50
    }

    [Fact]
    public void NoSourceRect_UsesRawScale_NinePatchAndPreSizedTexturePath()
    {
        // With no source rectangle the size-fit is undefined (nine-patch textures are pre-rendered at
        // Size), so the raw Scale applies — unchanged by the fix (it only touches the source-rect branch).
        var dc = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            SourceRectangle = null,
            Size = new Vector2(200f, 100f),
            Scale = new Vector2(2f, 3f),
        };

        Assert.Equal(new Vector2(2f, 3f), MasterRenderSystem.ComputeSpriteScale(dc));
    }
}
