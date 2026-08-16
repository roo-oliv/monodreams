using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Draw;

namespace MonoDreams.UI;

/// <summary>
/// Marks an entity as HIGHLIGHTED: <see cref="HighlightSystem"/> keeps a pulsing outline drawn
/// around whatever that entity draws — a sprite, a text label, a button, or any other
/// <c>DrawComponent</c>. Pure data; the system owns the overlay entity, its geometry, its depth
/// and its lifetime.
///
/// <para>
/// The primitive is deliberately generic (tutorial "click THIS", onboarding, quest hints,
/// accessibility emphasis, eyeballing an entity during a debug session). Nothing here knows what
/// the highlight MEANS — game code adds the component when it wants attention drawn and removes it
/// when it doesn't.
/// </para>
///
/// <para>
/// Zero-valued fields mean "use the system default", exactly like
/// <see cref="SimpleButtonComponent.LayerDepth"/> / <c>VisualScale</c> — so
/// <c>entity.Set&lt;HighlightComponent&gt;()</c> (which stores <c>default</c>, bypassing this
/// struct's field initializers) still produces a visible outline. Use
/// <c>entity.Set(new HighlightComponent())</c> to get the pulsing defaults below.
/// </para>
/// </summary>
public struct HighlightComponent()
{
    /// <summary>
    /// Peak outline colour (the brightest point of the pulse). Alpha 0 (an unset field) means
    /// "use the system default" (<c>Color.Gold</c>). The outline is ALWAYS drawn fully opaque —
    /// the mesh path composites premultiplied alpha, so a partial-alpha fill renders brighter, not
    /// dimmer (see the rendering premise "The mesh render path uses premultiplied alpha"). Encode
    /// dimness in the RGB channels (or in <see cref="PulseMinIntensity"/>), never in alpha.
    /// </summary>
    public Color Color = Color.Gold;

    /// <summary>
    /// Pulse frequency in cycles per second. <c>0</c> (or negative) disables the pulse: the outline
    /// is drawn steadily at the full <see cref="Color"/>.
    /// </summary>
    public float PulseSpeed = 1f;

    /// <summary>
    /// RGB multiplier at the trough of the pulse, clamped to <c>0..1</c>. <c>1</c> is no visible
    /// pulse; <c>0</c> fades the outline to black at the trough. Ignored when
    /// <see cref="PulseSpeed"/> is not positive.
    /// </summary>
    public float PulseMinIntensity = 0.35f;

    /// <summary>Outline stroke thickness in world/screen units. <c>0</c> (unset) means the system
    /// default (2).</summary>
    public float Thickness = 2f;

    /// <summary>How far the outline sits OUTSIDE the target's drawn bounds, along the bounds' own
    /// axes. <c>0</c> hugs the bounds exactly.</summary>
    public float Padding = 3f;

    /// <summary>
    /// Explicit bounds, top-left anchored at the entity's <c>TransformComponent.WorldPosition</c>
    /// (the same convention as <see cref="FocusableComponent.Size"/>). <see cref="Vector2.Zero"/>
    /// (the default) means "derive the bounds from what the entity draws" — the normal case, and
    /// the only one that follows a sprite's scale or a label's measured width. Set this to
    /// highlight an entity that draws nothing (an invisible hotspot) or to override a derived box
    /// that isn't the shape you want.
    /// </summary>
    public Vector2 Size = Vector2.Zero;

    /// <summary>
    /// Render target for the outline. <c>null</c> (the default) inherits the target's own
    /// <c>DrawComponent.Target</c> each frame, so the highlight always composites in the same pass
    /// as the thing it highlights (falling back to <see cref="RenderTargetID.Main"/> when the
    /// entity has no <c>DrawComponent</c>).
    /// </summary>
    public RenderTargetID? Target = null;

    /// <summary>
    /// Added to the target's current <c>DrawComponent.LayerDepth</c> to place the outline just in
    /// front of it, re-derived every frame so the highlight survives z restacks. <c>0</c> (unset)
    /// means the system default (0.001); a negative value puts the outline BEHIND its target.
    /// Never leave the two at exactly equal depth — same-depth meshes fall through to
    /// insertion order (see the ui premise on <see cref="SimpleButtonComponent.LayerDepth"/>).
    /// </summary>
    public float LayerDepthOffset = 0.001f;

    /// <summary>
    /// Depth used when the entity has no <c>DrawComponent</c> to re-derive from (the explicit
    /// <see cref="Size"/> case). <c>0</c> (unset) means the system default (0.99 — in front of
    /// ordinary content in this painter's-order pipeline).
    /// </summary>
    public float FallbackLayerDepth = 0.99f;

    /// <summary>
    /// Owned by <see cref="HighlightSystem"/>: the overlay entity that draws the outline. Written
    /// by the system when it creates (or re-creates) the overlay; read it to inspect the outline's
    /// <c>DrawComponent</c>. Never assign it — the system disposes what it created.
    /// </summary>
    public Entity Overlay = default;
}
