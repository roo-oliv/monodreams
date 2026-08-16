#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoDreams.System.Draw;

namespace MonoDreams.UI;

/// <summary>
/// Draws a pulsing outline around every entity carrying a <see cref="HighlightComponent"/>, on an
/// overlay entity the system owns end to end. Three invariants make this survive real screens
/// (each one is a bug an ad-hoc "glow" re-discovers):
///
/// <list type="number">
/// <item><b>Follow</b> — the outline is rebuilt every frame from the target's OWN drawn bounds
/// (its prepared <c>DrawComponent</c>), so it tracks position, scale, rotation, layout moves, text
/// that grew, and a button's press "pop" for free.</item>
/// <item><b>Depth</b> — the overlay's <c>LayerDepth</c> is re-derived every frame as the target's
/// current depth plus <see cref="HighlightComponent.LayerDepthOffset"/>, so a z restack (papers
/// shuffling on a desk, a Y-sort re-order) never leaves the glow under a sibling.</item>
/// <item><b>Lifetime</b> — the overlay is <c>ChildOfComponent</c>-parented to its target and is
/// disposed the moment the target dies or the <see cref="HighlightComponent"/> is removed, so
/// nothing pulses over empty space.</item>
/// </list>
///
/// <para>
/// <b>Pipeline placement:</b> register it at the END of the draw-prep stage — after
/// <c>SpritePrepSystem</c> / <c>YSortSystem</c> / <c>TextPrepSystem</c> / <c>MeshPrepSystem</c> /
/// <see cref="ButtonMeshPrepSystem"/> and before <c>MasterRenderSystem</c>. It reads the bounds and
/// the depth those systems just wrote, so running it earlier outlines last frame's state.
/// </para>
///
/// <para>
/// The overlay is a bare mesh entity: <c>DrawComponent</c> (world-space vertices +
/// <c>Matrix.Identity</c>, the <see cref="ButtonMeshPrepSystem"/> contract),
/// <c>ChildOfComponent</c>, <c>EntityInfoComponent("Highlight")</c>, and <c>VisibleComponent</c>
/// mirrored from the target. It deliberately carries NO <c>TransformComponent</c> — its geometry
/// is derived, not posed — which also puts it outside <c>MeshPrepSystem</c>'s query, so no
/// ordering accident can overwrite the identity world matrix its baked vertices need.
/// </para>
/// </summary>
public sealed class HighlightSystem : ISystem<GameState>
{
    /// <summary>Outline colour used when <see cref="HighlightComponent.Color"/> is left unset
    /// (alpha 0).</summary>
    public static readonly Color DefaultColor = Color.Gold;

    /// <summary>Stroke thickness used when <see cref="HighlightComponent.Thickness"/> is unset.</summary>
    public const float DefaultThickness = 2f;

    /// <summary>Depth epsilon used when <see cref="HighlightComponent.LayerDepthOffset"/> is unset.</summary>
    public const float DefaultLayerDepthOffset = 0.001f;

    /// <summary>Depth used when <see cref="HighlightComponent.FallbackLayerDepth"/> is unset AND the
    /// target has no <c>DrawComponent</c> to re-derive from.</summary>
    public const float DefaultFallbackLayerDepth = 0.99f;

    /// <summary><c>EntityInfoComponent.Type</c> of every overlay this system creates.</summary>
    public const string OverlayEntityType = "Highlight";

    private readonly World _world;
    private readonly EntitySet _targets;

    // target -> overlay. The system's own bookkeeping, and the ONLY way to reach an overlay whose
    // target has already been disposed (a dead entity carries no components to read a handle off).
    private readonly Dictionary<Entity, Entity> _overlays = new();
    private readonly List<Entity> _dropped = [];

    public bool IsEnabled { get; set; } = true;

    public HighlightSystem(World world)
    {
        _world = world;
        _targets = world.GetEntities().With<HighlightComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Lifetime first: an overlay whose target died (or lost its HighlightComponent) goes away
        // this frame, before anything else can read it. HierarchySystem's orphan cascade does the
        // same job through ChildOfComponent when it is in the pipeline; this sweep is what makes
        // the guarantee hold in a screen that never registers it.
        SweepOrphans();

        foreach (var entity in _targets.GetEntities())
            UpdateHighlight(state, entity);
    }

    private void SweepOrphans()
    {
        if (_overlays.Count == 0) return;

        foreach (var (target, overlay) in _overlays)
        {
            if (target.IsAlive && target.Has<HighlightComponent>() && overlay.IsAlive) continue;
            if (overlay.IsAlive) overlay.Dispose();
            _dropped.Add(target);
        }

        foreach (var target in _dropped) _overlays.Remove(target);
        _dropped.Clear();
    }

    private void UpdateHighlight(GameState state, in Entity entity)
    {
        ref var highlight = ref entity.Get<HighlightComponent>();
        var hasDraw = entity.Has<DrawComponent>();

        var renderTarget = highlight.Target
            ?? (hasDraw ? entity.Get<DrawComponent>().Target : RenderTargetID.Main);

        var overlay = EnsureOverlay(entity, ref highlight, renderTarget);
        ref var draw = ref overlay.Get<DrawComponent>();

        // Target feeds MasterRenderSystem's value-predicate draw sets, which re-evaluate only on
        // publication (foundation premise "A value-predicate EntitySet re-evaluates only when the
        // component is published") — so a retarget must be re-published, and ONLY a retarget: this
        // runs per highlight per frame and an unconditional notify would re-run every pass's
        // predicate each frame.
        if (draw.Target != renderTarget)
        {
            draw.Target = renderTarget;
            overlay.NotifyChanged<DrawComponent>();
        }

        // Visibility mirrors the target's: on the Main target that is the culling / show-hide
        // switch (CullingSystem never sees the overlay — it has no SpriteInfoComponent — so the
        // producer owns its tag, per the rendering premise "Main-target TEXT (and bare meshes) get
        // VisibleComponent from whoever spawns them"). UI/HUD/Scroll ignore the tag entirely, so
        // mirroring is a no-op there.
        MirrorVisibility(entity, overlay);

        // Depth, re-derived every frame — this is what survives a z restack.
        draw.LayerDepth = ResolveLayerDepth(entity, in highlight, hasDraw);

        var quad = ResolveQuad(entity, in highlight, hasDraw);
        if (quad == null)
        {
            // Nothing measurable to outline this frame (the target draws nothing, or its mesh is
            // empty). Empty the mesh rather than leave a stale outline: MasterRenderSystem skips a
            // DrawComponent with no geometry, on every render target.
            draw.Type = DrawElementType.Mesh;
            draw.Vertices = [];
            draw.Indices = [];
            return;
        }

        ExpandQuad(quad, highlight.Padding);

        var thickness = highlight.Thickness > 0f ? highlight.Thickness : DefaultThickness;
        var color = PulseColor(in highlight, state.TotalTime);

        draw.SetMeshData(new PolygonOutlineMeshGenerator(quad, thickness, color));
        // The vertices above are already in world space, so the renderer must use them as-is.
        draw.WorldMatrix = Matrix.Identity;
    }

    private Entity EnsureOverlay(in Entity target, ref HighlightComponent highlight, RenderTargetID renderTarget)
    {
        if (_overlays.TryGetValue(target, out var overlay) && overlay.IsAlive)
        {
            highlight.Overlay = overlay;
            return overlay;
        }

        overlay = _world.CreateEntity();
        overlay.Set(new EntityInfoComponent(OverlayEntityType));
        // Structural ownership: HierarchySystem cascade-disposes an orphan whose parent died, and
        // the overlay shows up nested under its target in the inspector.
        overlay.Set(new ChildOfComponent(target));
        overlay.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = renderTarget,
            WorldMatrix = Matrix.Identity,
        });

        _overlays[target] = overlay;
        highlight.Overlay = overlay;
        return overlay;
    }

    private static void MirrorVisibility(in Entity target, in Entity overlay)
    {
        if (target.Has<VisibleComponent>())
        {
            if (!overlay.Has<VisibleComponent>()) overlay.Set<VisibleComponent>();
        }
        else if (overlay.Has<VisibleComponent>())
        {
            overlay.Remove<VisibleComponent>();
        }
    }

    private static float ResolveLayerDepth(in Entity entity, in HighlightComponent highlight, bool hasDraw)
    {
        if (!hasDraw)
        {
            var fallback = highlight.FallbackLayerDepth > 0f
                ? highlight.FallbackLayerDepth
                : DefaultFallbackLayerDepth;
            return MathHelper.Clamp(fallback, 0f, 1f);
        }

        var offset = highlight.LayerDepthOffset != 0f ? highlight.LayerDepthOffset : DefaultLayerDepthOffset;
        return MathHelper.Clamp(entity.Get<DrawComponent>().LayerDepth + offset, 0f, 1f);
    }

    private static Vector2[]? ResolveQuad(in Entity entity, in HighlightComponent highlight, bool hasDraw)
    {
        // An explicit Size wins: it is the escape hatch for an entity that draws nothing (an
        // invisible hotspot) or whose drawn bounds are not the box you want outlined.
        if (highlight.Size != Vector2.Zero)
        {
            if (!entity.Has<TransformComponent>()) return null;
            var topLeft = entity.Get<TransformComponent>().WorldPosition;
            return AxisAlignedQuad(topLeft, highlight.Size);
        }

        return hasDraw ? DrawnQuad(entity.Get<DrawComponent>()) : null;
    }

    /// <summary>
    /// The four world-space corners — top-left, top-right, bottom-right, bottom-left, rotated with
    /// the element — of what <paramref name="draw"/> draws this frame, or <c>null</c> when it draws
    /// nothing measurable. Reads the PREPARED <c>DrawComponent</c> (what the renderer is about to
    /// submit), which is what makes one derivation cover sprites, nine-patches, text and meshes —
    /// including a button, whose outline+fill mesh <see cref="ButtonMeshPrepSystem"/> has already
    /// baked in world space.
    ///
    /// <para>
    /// Sprites reuse <c>MasterRenderSystem.ComputeSpriteScale</c>, so the outlined quad is the SAME
    /// quad the renderer draws (see the rendering premise "A sprite's drawn quad honors
    /// <c>Transform.WorldScale</c> exactly once"). Meshes have no canonical quad, so they measure
    /// as the axis-aligned bounding box of their vertices under their world matrix.
    /// </para>
    /// </summary>
    public static Vector2[]? DrawnQuad(DrawComponent draw)
    {
        switch (draw.Type)
        {
            case DrawElementType.Sprite:
            case DrawElementType.NinePatch:
            {
                var scale = MasterRenderSystem.ComputeSpriteScale(draw);
                var sourceSize = draw.SourceRectangle is { Width: > 0, Height: > 0 } source
                    ? new Vector2(source.Width, source.Height)
                    : draw.Texture != null
                        ? new Vector2(draw.Texture.Width, draw.Texture.Height)
                        : draw.Size;
                return PivotedQuad(draw.Position, -draw.Origin * scale, sourceSize * scale, draw.Rotation);
            }

            case DrawElementType.Text:
            {
                // TextPrepSystem writes the MEASURED extent into Size and the composed
                // (world × text) scale into Scale, and clears Text when the label renders nothing.
                if (string.IsNullOrEmpty(draw.Text)) return null;
                return PivotedQuad(draw.Position, -draw.Origin * draw.Scale, draw.Size * draw.Scale, draw.Rotation);
            }

            case DrawElementType.Mesh:
            {
                if (!draw.HasValidMesh) return null;
                var matrix = draw.WorldMatrix ?? Matrix.Identity;
                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);

                if (draw.Vertices is { Length: > 0 } vertices)
                {
                    foreach (var vertex in vertices)
                        Accumulate(Vector2.Transform(new Vector2(vertex.Position.X, vertex.Position.Y), matrix),
                            ref min, ref max);
                }
                else if (draw.TexturedVertices is { Length: > 0 } textured)
                {
                    foreach (var vertex in textured)
                        Accumulate(Vector2.Transform(new Vector2(vertex.Position.X, vertex.Position.Y), matrix),
                            ref min, ref max);
                }

                return max.X < min.X ? null : AxisAlignedQuad(min, max - min);
            }

            default:
                return null;
        }
    }

    private static void Accumulate(Vector2 point, ref Vector2 min, ref Vector2 max)
    {
        min = Vector2.Min(min, point);
        max = Vector2.Max(max, point);
    }

    private static Vector2[] AxisAlignedQuad(Vector2 topLeft, Vector2 size) =>
    [
        topLeft,
        new(topLeft.X + size.X, topLeft.Y),
        topLeft + size,
        new(topLeft.X, topLeft.Y + size.Y),
    ];

    /// <summary>The quad of a <c>SpriteBatch</c>-style draw: <paramref name="size"/> laid out from
    /// <paramref name="offset"/> relative to <paramref name="pivot"/>, then rotated about the pivot
    /// (which is exactly how <c>SpriteBatch.Draw</c> applies position / origin / rotation).</summary>
    private static Vector2[] PivotedQuad(Vector2 pivot, Vector2 offset, Vector2 size, float rotation)
    {
        var quad = AxisAlignedQuad(offset, size);
        if (rotation == 0f)
        {
            for (var i = 0; i < quad.Length; i++) quad[i] += pivot;
            return quad;
        }

        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        for (var i = 0; i < quad.Length; i++)
        {
            var corner = quad[i];
            quad[i] = pivot + new Vector2(corner.X * cos - corner.Y * sin, corner.X * sin + corner.Y * cos);
        }
        return quad;
    }

    /// <summary>Pushes each corner outward by <paramref name="padding"/> along the quad's OWN axes,
    /// so the outline keeps hugging a rotated target instead of ballooning along the screen axes.</summary>
    private static void ExpandQuad(Vector2[] quad, float padding)
    {
        if (padding == 0f) return;

        var u = quad[1] - quad[0];
        var v = quad[3] - quad[0];
        u = u.LengthSquared() > 0f ? Vector2.Normalize(u) : Vector2.UnitX;
        v = v.LengthSquared() > 0f ? Vector2.Normalize(v) : Vector2.UnitY;

        var du = u * padding;
        var dv = v * padding;
        quad[0] += -du - dv;
        quad[1] += du - dv;
        quad[2] += du + dv;
        quad[3] += -du + dv;
    }

    /// <summary>
    /// The outline colour for <paramref name="totalTime"/>: <see cref="HighlightComponent.Color"/>
    /// scaled between <see cref="HighlightComponent.PulseMinIntensity"/> and full by a sine at
    /// <see cref="HighlightComponent.PulseSpeed"/> Hz. The result is always fully OPAQUE — the mesh
    /// path composites premultiplied alpha, so pulsing the alpha channel would brighten the
    /// outline instead of fading it (rendering premise "The mesh render path uses premultiplied
    /// alpha — UI fills must be opaque"). The pulse therefore lives in the RGB channels.
    /// </summary>
    public static Color PulseColor(in HighlightComponent highlight, float totalTime)
    {
        var baseColor = highlight.Color.A == 0 ? DefaultColor : highlight.Color;
        if (highlight.PulseSpeed <= 0f)
            return new Color((int)baseColor.R, baseColor.G, baseColor.B, 255);

        var minimum = MathHelper.Clamp(highlight.PulseMinIntensity, 0f, 1f);
        var phase = 0.5f * (1f + MathF.Sin(totalTime * highlight.PulseSpeed * MathHelper.TwoPi));
        var intensity = MathHelper.Lerp(minimum, 1f, phase);

        return new Color(
            (int)(baseColor.R * intensity),
            (int)(baseColor.G * intensity),
            (int)(baseColor.B * intensity),
            255);
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
            if (overlay.IsAlive)
                overlay.Dispose();

        _overlays.Clear();
        _targets.Dispose();
    }
}
