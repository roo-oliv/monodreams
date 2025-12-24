using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Layout;

/// <summary>
/// Style configuration for buttons created via the layout builder.
/// </summary>
public class ButtonStyle
{
    /// <summary>
    /// Default text color.
    /// </summary>
    public Color DefaultColor { get; set; } = Color.White;

    /// <summary>
    /// Text color when hovered.
    /// </summary>
    public Color HoveredColor { get; set; } = Color.Yellow;

    /// <summary>
    /// Text color when disabled.
    /// </summary>
    public Color DisabledColor { get; set; } = Color.Gray;

    /// <summary>
    /// Border/outline color.
    /// </summary>
    public Color BorderColor { get; set; } = Color.White;

    /// <summary>
    /// Border thickness in pixels.
    /// </summary>
    public float BorderThickness { get; set; } = 2f;

    /// <summary>
    /// Padding inside the button (space between border and text).
    /// </summary>
    public float Padding { get; set; } = 10f;

    /// <summary>
    /// Text scale factor.
    /// </summary>
    public float TextScale { get; set; } = 0.15f;

    /// <summary>
    /// Creates a default button style.
    /// </summary>
    public static ButtonStyle Default => new();

    /// <summary>
    /// Creates a button style with the specified colors.
    /// </summary>
    public static ButtonStyle WithColors(Color defaultColor, Color hoveredColor, Color disabledColor) => new()
    {
        DefaultColor = defaultColor,
        HoveredColor = hoveredColor,
        DisabledColor = disabledColor,
        BorderColor = defaultColor
    };
}
