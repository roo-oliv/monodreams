using DefaultEcs;

namespace MonoDreams.UI;

/// <summary>
/// THE pointer pick: which entity the pointer is over this frame, published on the CURSOR entity by
/// <see cref="UIFocusSystem"/> (the module's single pointer-vs-<see cref="FocusableComponent"/>
/// hit-test) and read by every system that reacts to what the pointer is over —
/// <see cref="TooltipSystem"/> and <see cref="CursorHoverSystem"/> today. Pure data; nothing but the
/// pick's owner writes it.
///
/// <para><see cref="Hovered"/> is the topmost focusable under the pointer that focus and click would
/// act on (same filters: in the active group, not tab-gated, not control-disabled), or
/// <c>default</c> when the pointer is over nothing. <see cref="HoverStartTime"/> is the
/// <c>GameState.TotalTime</c> at which that entity BECAME the hovered one, so a consumer gets the
/// dwell time for free (<c>state.TotalTime - HoverStartTime</c>) without keeping its own timer — the
/// tooltip's hover delay is exactly that subtraction.</para>
///
/// <para>Consumers must treat <see cref="Hovered"/> as untrusted: the pick is only refreshed while
/// its owner runs, so a consumer always re-checks <c>Hovered.IsAlive</c> (a hovered entity can be
/// disposed the same frame — see the ui premise "There is ONE pointer pick").</para>
/// </summary>
public struct PointerPickComponent
{
    /// The topmost focusable under the pointer this frame, or <c>default</c> for "nothing picked".
    public Entity Hovered;

    /// <c>GameState.TotalTime</c> when <see cref="Hovered"/> became the hovered entity. Stays put
    /// while the same entity keeps the pick, so dwell time is <c>TotalTime - HoverStartTime</c>.
    public float HoverStartTime;
}
