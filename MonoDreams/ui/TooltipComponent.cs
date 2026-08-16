using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// "Hover me and read this." Pure data on any PICKABLE entity — i.e. any entity carrying a
/// <see cref="FocusableComponent"/>, whatever render target it lives on — declaring the label
/// <see cref="TooltipSystem"/> floats next to the pointer while that entity holds the pointer pick
/// (<see cref="PointerPickComponent"/>). The entity needs nothing else: no extra hit-box, no
/// visibility bookkeeping, no per-screen show/hide wiring.
///
/// <para>A control that is not picked never shows its tooltip. That is deliberate — the pick is the
/// SAME resolution focus and click use — so a tab-gated
/// (<see cref="FocusableComponent.Disabled"/>) or control-disabled
/// (<see cref="ButtonStateComponent.IsDisabled"/>) entity has no tooltip today. See the ui premise
/// "There is ONE pointer pick" for why, and the "Tooltips on unpickable controls" open question
/// beneath it.</para>
/// </summary>
public struct TooltipComponent
{
    /// The label to show. Empty / null means "no tooltip" — a cheap way to mute one without
    /// removing the component. Single-line is the intended shape (a tooltip explains, it doesn't
    /// document); embedded '\n' still renders, laid out by the text path's line spacing.
    public string Text;

    /// Seconds the pointer must rest on this entity before the label appears. <c>null</c> (the
    /// default) means "use the system's <see cref="TooltipStyle.Delay"/>"; <c>0</c> means show
    /// instantly. An instant tooltip on every pointer crossing feels like a mosquito, hence the
    /// dwell.
    public float? Delay;
}

/// <summary>
/// Look and feel of the floating label <see cref="TooltipSystem"/> spawns: the dwell before it
/// appears, the text scale/color, the panel's padding / corner radius / fill / outline, the offset
/// from the pointer, the screen margin the edge-flip keeps, and the layer depth it draws at. One
/// style per system instance (like <see cref="ButtonTheme"/> for buttons) — a screen that wants two
/// tooltip looks registers two systems over disjoint entities, but the framework's answer is one.
///
/// <para>Fills are OPAQUE by default on purpose: the mesh render path uses premultiplied alpha, so a
/// partial-alpha fill renders far brighter than intended (see the rendering premises).</para>
/// </summary>
public sealed class TooltipStyle
{
    /// Default dwell (seconds) before an entity's tooltip appears, used when
    /// <see cref="TooltipComponent.Delay"/> is null.
    public float Delay { get; set; } = 0.4f;

    /// Text scale applied to the supplied font (the ui demo's body text sits around 0.18).
    public float TextScale { get; set; } = 0.18f;

    /// Text color of the label.
    public Color TextColor { get; set; } = new(232, 236, 244);

    /// Padding between the label text and the panel edge (x = horizontal, y = vertical).
    public Vector2 Padding { get; set; } = new(10f, 6f);

    /// Corner radius of the panel.
    public float CornerRadius { get; set; } = 6f;

    /// Opaque panel fill.
    public Color Fill { get; set; } = new(28, 32, 44);

    /// Panel outline color. Transparent (or a zero <see cref="OutlineThickness"/>) draws no outline.
    public Color Outline { get; set; } = new(96, 106, 128);

    /// Panel outline thickness in pixels; 0 draws no outline.
    public float OutlineThickness { get; set; } = 1.5f;

    /// Offset of the panel's top-left from the pointer, before any edge flip. The default puts the
    /// label below-right of the cursor, clear of a standard arrow silhouette.
    public Vector2 Offset { get; set; } = new(16f, 20f);

    /// Margin kept between the panel and the screen edges — the edge-flip / clamp budget.
    public float ScreenMargin { get; set; } = 6f;

    /// Layer depth of the panel; the label text draws just above it. Keep it below the cursor's
    /// depth (the cursor factory uses 1.0) so the pointer stays on top of its own tooltip.
    public float LayerDepth { get; set; } = 0.98f;

    /// A fresh style with the defaults above.
    public static TooltipStyle Default => new();
}
