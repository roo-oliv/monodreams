using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// The four common button looks. <see cref="Primary"/> is a solid filled call-to-action,
/// <see cref="Secondary"/> an outlined button, <see cref="Tertiary"/> a quiet ghost button
/// (fill appears only on hover/press), and <see cref="Link"/> a text-only link.
/// </summary>
public enum ButtonVariant
{
    Primary,
    Secondary,
    Tertiary,
    Link,
}

/// <summary>
/// State + identity for an interactive button, layered on top of the visual
/// <see cref="SimpleButtonComponent"/> exactly the way <c>ToggleSwitchComponent</c> layers on a
/// checkbox. <see cref="UIFocusSystem"/> sets <see cref="IsPressed"/> from input; the game sets
/// <see cref="IsDisabled"/>; <see cref="ButtonVisualSystem"/> reads this plus the entity's
/// <see cref="FocusableComponent"/> (hover/focus) to resolve per-state colors and the click "pop"
/// scale. <see cref="Id"/> is echoed in <see cref="UIFocusActivated"/> so the screen routes the
/// click — the dispatch stays game-owned.
/// </summary>
public struct ButtonStateComponent
{
    /// Identifier echoed in <see cref="UIFocusActivated"/> for game-side routing.
    public string Id;

    /// Which look <see cref="ButtonVisualSystem"/> paints.
    public ButtonVariant Variant;

    /// When true the button paints its disabled colors and never activates. Disabled wins.
    public bool IsDisabled;

    /// Set by <see cref="UIFocusSystem"/> while the pointer holds the button. Drives the press pop.
    public bool IsPressed;

    /// Persistent selected/highlighted state independent of hover — e.g. the active tab in a
    /// <c>TabBar</c> or the current item in a list. <see cref="ButtonVisualSystem"/> paints it with
    /// the hover look so it reads as "current" even when nothing is focused.
    public bool IsActive;

    /// Animated by <see cref="ButtonVisualSystem"/> and applied by <c>ButtonMeshPrepSystem</c> to
    /// scale the quad around its centre (a subtle press "pop"). 0 is treated as 1 (no scale), so a
    /// button created without touching this field renders at full size.
    public float VisualScale;
}

/// <summary>
/// Resolved colors for one button state (idle / hover / pressed / disabled). A zero-alpha color
/// means "draw nothing" for that slot — e.g. a Link button has transparent outline and fill.
/// </summary>
public readonly record struct ButtonStateColors(Color Outline, Color Fill, Color Text);

/// <summary>The four state colors for a single <see cref="ButtonVariant"/>.</summary>
public sealed class ButtonVariantColors
{
    public ButtonStateColors Normal { get; set; }
    public ButtonStateColors Hover { get; set; }
    public ButtonStateColors Pressed { get; set; }
    public ButtonStateColors Disabled { get; set; }
}

/// <summary>
/// Palette mapping each <see cref="ButtonVariant"/> to its per-state colors. <see cref="Default"/>
/// is a self-contained blue-accent theme so a button "just works" from <see cref="ButtonVariant"/>
/// alone; a game can supply its own (e.g. the demo's palette) when constructing
/// <see cref="ButtonVisualSystem"/>.
/// </summary>
public sealed class ButtonTheme
{
    public ButtonVariantColors Primary { get; set; } = new();
    public ButtonVariantColors Secondary { get; set; } = new();
    public ButtonVariantColors Tertiary { get; set; } = new();
    public ButtonVariantColors Link { get; set; } = new();

    /// Outline color drawn around the focused/active control so keyboard focus is visible across
    /// every variant (including text-only Link buttons). <see cref="ButtonVisualSystem"/> forces a
    /// ring of <see cref="FocusRingThickness"/> when the button is focused or active.
    public Color FocusRingColor { get; set; } = new(250, 210, 120);
    public float FocusRingThickness { get; set; } = 2.5f;

    public ButtonVariantColors For(ButtonVariant variant) => variant switch
    {
        ButtonVariant.Primary => Primary,
        ButtonVariant.Secondary => Secondary,
        ButtonVariant.Tertiary => Tertiary,
        ButtonVariant.Link => Link,
        _ => Primary,
    };

    public static ButtonTheme Default
    {
        get
        {
            var accent = new Color(79, 140, 224);
            var accentLight = new Color(111, 163, 232);
            var accentDark = new Color(58, 107, 181);
            var onAccent = new Color(244, 246, 250);
            // Opaque hover/press tints. The mesh render path uses premultiplied alpha, so a
            // partial-alpha fill renders far brighter than intended — keep fills fully opaque and
            // express subtlety through the color value, not the alpha.
            var ghost = new Color(60, 84, 132);
            var ghostStrong = new Color(84, 112, 168);
            var disFill = new Color(108, 112, 122);
            var disText = new Color(150, 154, 164);
            var disOutline = new Color(90, 94, 104);
            var clear = Color.Transparent;

            return new ButtonTheme
            {
                Primary = new ButtonVariantColors
                {
                    Normal = new(clear, accent, onAccent),
                    Hover = new(clear, accentLight, onAccent),
                    Pressed = new(clear, accentDark, onAccent),
                    Disabled = new(clear, disFill, disText),
                },
                Secondary = new ButtonVariantColors
                {
                    Normal = new(accent, clear, accent),
                    Hover = new(accentLight, ghost, accentLight),
                    Pressed = new(accentDark, ghostStrong, accentDark),
                    Disabled = new(disOutline, clear, disText),
                },
                Tertiary = new ButtonVariantColors
                {
                    Normal = new(clear, clear, accent),
                    Hover = new(clear, ghost, accentLight),
                    Pressed = new(clear, ghostStrong, accentDark),
                    Disabled = new(clear, clear, disText),
                },
                Link = new ButtonVariantColors
                {
                    Normal = new(clear, clear, accent),
                    Hover = new(clear, clear, accentLight),
                    Pressed = new(clear, clear, accentDark),
                    Disabled = new(clear, clear, disText),
                },
            };
        }
    }
}
