using Microsoft.Xna.Framework;

namespace MonoDreams.Demos.UI;

/// A clickable menu button used by every demo screen. Generic over outcome:
/// the click handler is identified by the `Id` field; the owning screen
/// subscribes to <see cref="DemoButtonClicked"/> and dispatches by id.
public struct DemoButtonComponent
{
    public string Id;
    public bool IsHovered;
    /// When true, the button stays highlighted with ActiveColor regardless of hover.
    /// Used by demos to show the currently-selected option in a list (e.g. active camera mode).
    public bool IsActive;
    /// When true, the button paints its Disabled colors, never hovers, and never dispatches
    /// a click. Disabled wins over Active and Hover.
    public bool IsDisabled;
    public Color DefaultColor;
    public Color HoveredColor;
    public Color ActiveColor;
    /// Text + outline color while disabled.
    public Color DisabledColor;
    /// Optional background fill colors for the button's <see cref="SimpleButtonComponent"/>.
    /// When DefaultFillColor.A == 0 the fill is left untouched by the interaction system.
    public Color DefaultFillColor;
    public Color HoveredFillColor;
    public Color ActiveFillColor;
    public Color DisabledFillColor;
    /// Optional constant text color. When its alpha > 0 the linked text keeps this color in
    /// every (enabled) state, while the outline still tracks Default/Hover/Active — used by the
    /// grey-fill menu buttons that want dark text + a constant grey border and fill-only states.
    public Color TextColorOverride;
}

public readonly record struct DemoButtonClicked(string Id);
