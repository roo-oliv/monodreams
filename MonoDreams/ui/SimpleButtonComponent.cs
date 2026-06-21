using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Draw;

namespace MonoDreams.UI;

/// <summary>
/// Component that holds a button's properties.
/// </summary>
public struct SimpleButtonComponent
{
    public Vector2 Size { get; set; }
    public float LineThickness { get; set; }
    public Color Color { get; set; }
    /// Optional solid fill behind the outline. Transparent (alpha 0) disables the fill.
    public Color FillColor { get; set; }
    public Entity? TextEntity { get; set; }
    public RenderTargetID Target { get; set; }
    /// Layer depth for the button's outline + fill mesh, honored by <c>ButtonMeshPrepSystem</c>.
    /// 0 (the default for an unset field) means "use the system default" (0.95). Set this LOWER to
    /// push the button's fill/ring behind sibling decorations that must stay visible over the fill
    /// (e.g. a checkbox row's box and checkmark), keeping the depth ordering strict. Higher = drawn
    /// on top in this pipeline.
    public float LayerDepth { get; set; }
    /// Visual-only scale applied by <c>ButtonMeshPrepSystem</c> around the button's centre — a
    /// press "pop" driven by <c>ButtonVisualSystem</c>. The layout <see cref="Size"/> (and the
    /// hit-test) are unaffected, so the button geometry stays put while the drawn quad scales.
    /// 0 is treated as 1 (no scale), so buttons that never set it render at full size.
    public float VisualScale { get; set; }
}
